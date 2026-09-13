using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using static CuttingCounter;

public class StoveCounter : BaseCounter, IHasProgress
{
	public event EventHandler<IHasProgress.OnProgressChangedEventArgs> OnProgressChanged;
	public event EventHandler<OnStateChangedEventArgs> OnStateChanged;
	public class OnStateChangedEventArgs : EventArgs
	{
		public State state;
	}

	public enum State
	{
		Idle,
		Frying,
		Fried,
		Burned,
	}

	[SerializeField] private FryingRecipeSO[] fryingRecipeSOArray;
	[SerializeField] private BurningRecipeSO[] burningRecipeSOArray;

	private NetworkVariable<State> state = new NetworkVariable<State>(State.Idle);
	private NetworkVariable<float> fryingTimer = new NetworkVariable<float>(0f);
	private NetworkVariable<float> burningTimer = new NetworkVariable<float>(0f);
	private FryingRecipeSO fryingRecipeSO;
	private BurningRecipeSO burningRecipeSO;

	public override void OnNetworkSpawn()
	{
		base.OnNetworkSpawn();

		fryingTimer.OnValueChanged += FryingTimer_OnValueChanged;
		burningTimer.OnValueChanged += BurningTimer_OnValueChanged;
		state.OnValueChanged += State_OnValueChanged;

		State_OnValueChanged(state.Value, state.Value);
	}

	public override void OnNetworkDespawn()
	{
		fryingTimer.OnValueChanged -= FryingTimer_OnValueChanged;
		burningTimer.OnValueChanged -= BurningTimer_OnValueChanged;
		state.OnValueChanged -= State_OnValueChanged;

		base.OnNetworkDespawn();
	}

	private void FryingTimer_OnValueChanged(float previousValue, float newValue)
	{
		if (state.Value != State.Frying) return;
		float fryingTimerMax = fryingRecipeSO ? fryingRecipeSO.fryingTimerMax : 1f;
		SetProgress(newValue / fryingTimerMax);
	}

	private void BurningTimer_OnValueChanged(float previousValue, float newValue)
	{
		if (state.Value != State.Fried) return;
		float burningTimerMax = burningRecipeSO ? burningRecipeSO.burningTimerMax : 1f;
		SetProgress(newValue / burningTimerMax);
	}

	private void SetProgress(float progressNormalized)
	{
		OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs
		{
			progressNormalized = Mathf.Clamp01(progressNormalized)
		});
	}

	private void State_OnValueChanged(State previousValue, State newValue)
	{
		OnStateChanged?.Invoke(this, new OnStateChangedEventArgs
		{
			state = state.Value
		});

		//for hiding progress bar
		if (state.Value == State.Burned || state.Value == State.Idle)
		{
			OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs
			{
				progressNormalized = 0f
			});
		}
	}

	private void Update()
	{
		if (!IsServer) return;

		if (HasKitchenObject())
		{
			switch (state.Value)
			{
			case State.Idle:
			break;
			case State.Frying:
				fryingTimer.Value += Time.deltaTime;

				if (fryingTimer.Value > fryingRecipeSO.fryingTimerMax)
				{
					// Fried
					// GetKitchenObject().DestroySelf();
					KitchenObject.DespawnKitchenObject(GetKitchenObject());

					KitchenObject.SpawnKitchenObject(fryingRecipeSO.output, this);

					state.Value = State.Fried;
					burningTimer.Value = 0f;
					SetBurningRecipeSOClientRpc(KitchenGameMultiplayer.Instance.GetKitchenObjectSOIndex(GetKitchenObject().GetKitchenObjectSO()));
					burningRecipeSO = GetBurningRecipeSOWithInput(GetKitchenObject().GetKitchenObjectSO());
				}
			break;
			case State.Fried:
				burningTimer.Value += Time.deltaTime;

				if (burningTimer.Value > burningRecipeSO.burningTimerMax)
				{
					// Fried
					// GetKitchenObject().DestroySelf();
					KitchenObject.DespawnKitchenObject(GetKitchenObject());

					KitchenObject.SpawnKitchenObject(burningRecipeSO.output, this);

					state.Value = State.Burned;
				}
			break;
			case State.Burned:
			break;
			}
		}
	}

	public override void Interact(Player player)
	{
		if (!HasKitchenObject())
		{
			// There is no KitchenObject here
			if (player.HasKitchenObject())
			{
				// Player is carrying something
				if (HasRecipeWithInput(player.GetKitchenObject().GetKitchenObjectSO()))
				{
					// Player carrying something that can be Fried
					var kitchenObject = player.GetKitchenObject();
					kitchenObject.SetKitchenObjectParent(this);

					InteractPlaceKitchenObjectOnStoveServerRpc(KitchenGameMultiplayer.Instance.GetKitchenObjectSOIndex(kitchenObject.GetKitchenObjectSO()));
				}
			}
			else
			{
				// Player not carrying anything
			}
		}
		else
		{
			// There is a KitchenObject here
			if (player.HasKitchenObject())
			{
				// Player is carrying something
				if (player.GetKitchenObject().TryGetPlate(out PlateKitchenObject plateKitchenObject))
				{
					// Player is holding a Plate
					if (plateKitchenObject.TryAddIngredient(GetKitchenObject().GetKitchenObjectSO()))
					{
						KitchenObject.DespawnKitchenObject(GetKitchenObject());
						SetStateIdleServerRpc();
					}
				}
			}
			else
			{
				// Player is not carrying anything
				GetKitchenObject().SetKitchenObjectParent(player);
				SetStateIdleServerRpc();
			}
		}
	}

	#region [ INTERACT PLACE RPC ]
	[ServerRpc(RequireOwnership = false)]
	private void InteractPlaceKitchenObjectOnStoveServerRpc(int kitchenObjectSOIndex)
	{
		KitchenObjectSO kitchenObjectSO = KitchenGameMultiplayer.Instance.GetKitchenObjectSOByIndex(kitchenObjectSOIndex);

		fryingRecipeSO = GetFryingRecipeSOWithInput(kitchenObjectSO);

		if (!fryingRecipeSO)
		{
			Debug.LogError($"No frying recipe found for {kitchenObjectSO.name}.", this);
			return;
		}

		fryingTimer.Value = 0f;
		burningTimer.Value = 0f;
		state.Value = State.Frying;

		SetFryingRecipeSOClientRpc(kitchenObjectSOIndex);
	}

	[ClientRpc]
	private void SetFryingRecipeSOClientRpc(int kitchenObjectSOIndex)
	{
		var kitchenObjectSO = KitchenGameMultiplayer.Instance.GetKitchenObjectSOByIndex(kitchenObjectSOIndex);
		fryingRecipeSO = GetFryingRecipeSOWithInput(kitchenObjectSO);
	}

	[ClientRpc]
	private void SetBurningRecipeSOClientRpc(int kitchenObjectSOIndex)
	{
		var kitchenObjectSO = KitchenGameMultiplayer.Instance.GetKitchenObjectSOByIndex(kitchenObjectSOIndex);
		burningRecipeSO = GetBurningRecipeSOWithInput(kitchenObjectSO);
	}
	#endregion

	#region [ STATE SERVERRPC ]
	[ServerRpc(RequireOwnership = false)]
	private void SetStateIdleServerRpc() { state.Value = State.Idle; }
	#endregion

	private bool HasRecipeWithInput(KitchenObjectSO inputKitchenObjectSO)
	{
		FryingRecipeSO fryingRecipeSO = GetFryingRecipeSOWithInput(inputKitchenObjectSO);
		return fryingRecipeSO != null;
	}

	private KitchenObjectSO GetOutputForInput(KitchenObjectSO inputKitchenObjectSO)
	{
		FryingRecipeSO fryingRecipeSO = GetFryingRecipeSOWithInput(inputKitchenObjectSO);
		if (fryingRecipeSO != null)
		{
			return fryingRecipeSO.output;
		}
		else
		{
			return null;
		}
	}

	private FryingRecipeSO GetFryingRecipeSOWithInput(KitchenObjectSO inputKitchenObjectSO)
	{
		foreach (FryingRecipeSO fryingRecipeSO in fryingRecipeSOArray)
		{
			if (fryingRecipeSO.input == inputKitchenObjectSO)
			{
				return fryingRecipeSO;
			}
		}
		return null;
	}

	private BurningRecipeSO GetBurningRecipeSOWithInput(KitchenObjectSO inputKitchenObjectSO)
	{
		foreach (BurningRecipeSO burningRecipeSO in burningRecipeSOArray)
		{
			if (burningRecipeSO.input == inputKitchenObjectSO)
			{
				return burningRecipeSO;
			}
		}
		return null;
	}

	public bool IsFried() { return state.Value == State.Fried; }
}
