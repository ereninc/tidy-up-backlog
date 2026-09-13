using System;
using System.Collections.Generic;
using System.Numerics;
using Unity.Netcode;
using UnityEngine;
using Quaternion = UnityEngine.Quaternion;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

public class Player : NetworkBehaviour, IKitchenObjectParent
{
	public static Player LocalInstance { get; private set; }

	public event EventHandler OnPickedSomething;
	public event EventHandler<OnSelectedCounterChangedEventArgs> OnSelectedCounterChanged;

	public static event EventHandler OnAnyPlayerSpawned;
	public static event EventHandler OnAnyPlayerPickedSomething;

	public class OnSelectedCounterChangedEventArgs : EventArgs
	{
		public BaseCounter selectedCounter;
	}

	[SerializeField] private float moveSpeed = 7f;
	[SerializeField] private LayerMask countersLayerMask;
	[SerializeField] private LayerMask collisionsLayerMask;
	[SerializeField] private Transform kitchenObjectHoldPoint;
	[SerializeField] private List<Vector3> spawnPositions;
	[SerializeField] private PlayerVisual playerVisual;

	private bool isWalking;
	private Vector3 lastInteractDir;
	private BaseCounter selectedCounter;
	private KitchenObject kitchenObject;

	private void Start()
	{
		if (GameInput.Instance)
		{
			var gameInput = GameInput.Instance;

			gameInput.OnInteractAction += GameInput_OnInteractAction;
			gameInput.OnInteractAlternateAction += GameInput_OnInteractAlternateAction;
		}

		PlayerData playerData = KitchenGameMultiplayer.Instance.GetPlayerDataFromClientId(OwnerClientId);
		playerVisual.SetPlayerColor(KitchenGameMultiplayer.Instance.GetPlayerColor(playerData.colorId));
	}

	public override void OnNetworkSpawn()
	{
		base.OnNetworkSpawn();
		if (IsOwner) LocalInstance = this;

		transform.position = spawnPositions[KitchenGameMultiplayer.Instance.GetPlayerDataIndexFromClientId(OwnerClientId)];

		OnAnyPlayerSpawned?.Invoke(this, EventArgs.Empty);

		if (IsServer)
		{
			NetworkManager.Singleton.OnClientDisconnectCallback += Singleton_OnClientDisconnectCallback;
		}
	}
	
	private void Singleton_OnClientDisconnectCallback(ulong clientId)
	{
		if (clientId == OwnerClientId && HasKitchenObject())
		{
			KitchenObject.DespawnKitchenObject(GetKitchenObject());
		}
	}

	private void GameInput_OnInteractAlternateAction(object sender, EventArgs e)
	{
		if (!KitchenGameManager.Instance.IsGamePlaying()) return;

		if (selectedCounter != null)
		{
			selectedCounter.InteractAlternate(this);
		}
	}

	private void GameInput_OnInteractAction(object sender, System.EventArgs e)
	{
		if (!KitchenGameManager.Instance.IsGamePlaying()) return;

		if (selectedCounter != null)
		{
			selectedCounter.Interact(this);
		}
	}

	private void Update()
	{
		if (!IsOwner) return;

		HandleMovement();
		HandleInteractions();
	}

	public bool IsWalking() { return isWalking; }

	private void HandleInteractions()
	{
		Vector2 inputVector = GameInput.Instance.GetMovementVectorNormalized();

		Vector3 moveDir = new Vector3(inputVector.x, 0f, inputVector.y);

		if (moveDir != Vector3.zero)
		{
			lastInteractDir = moveDir;
		}

		float interactDistance = 2f;
		if (Physics.Raycast(transform.position, lastInteractDir, out RaycastHit raycastHit, interactDistance, countersLayerMask))
		{
			if (raycastHit.transform.TryGetComponent(out BaseCounter baseCounter))
			{
				// Has ClearCounter
				if (baseCounter != selectedCounter)
				{
					SetSelectedCounter(baseCounter);
				}
			}
			else
			{
				SetSelectedCounter(null);
			}
		}
		else
		{
			SetSelectedCounter(null);
		}
	}

	private void HandleMovement()
	{
		Vector2 inputVector = GameInput.Instance.GetMovementVectorNormalized();

		Vector3 moveDir = new Vector3(inputVector.x, 0f, inputVector.y);

		float moveDistance = moveSpeed * Time.deltaTime;
		float playerRadius = .7f;
		float playerHeight = 2f;
		bool canMove = !Physics.BoxCast(transform.position, Vector3.one * playerRadius, moveDir, Quaternion.identity, moveDistance, collisionsLayerMask);

		if (!canMove)
		{
			// Cannot move towards moveDir

			// Attempt only X movement
			Vector3 moveDirX = new Vector3(moveDir.x, 0, 0).normalized;
			canMove = (moveDir.x < -.5f || moveDir.x > +.5f) && !Physics.BoxCast(transform.position, Vector3.one * playerRadius, moveDirX, Quaternion.identity, moveDistance, collisionsLayerMask);

			if (canMove)
			{
				// Can move only on the X
				moveDir = moveDirX;
			}
			else
			{
				// Cannot move only on the X

				// Attempt only Z movement
				Vector3 moveDirZ = new Vector3(0, 0, moveDir.z).normalized;
				canMove = (moveDir.z < -.5f || moveDir.z > +.5f) && !Physics.BoxCast(transform.position, Vector3.one * playerRadius, moveDirZ, Quaternion.identity, moveDistance, collisionsLayerMask);

				if (canMove)
				{
					// Can move only on the Z
					moveDir = moveDirZ;
				}
				else
				{
					// Cannot move in any direction
				}
			}
		}

		if (canMove)
		{
			transform.position += moveDir * moveDistance;
		}

		isWalking = moveDir != Vector3.zero;

		float rotateSpeed = 10f;
		if (isWalking) transform.forward = Vector3.Slerp(transform.forward, moveDir, Time.deltaTime * rotateSpeed);
	}

	private void SetSelectedCounter(BaseCounter selectedCounter)
	{
		this.selectedCounter = selectedCounter;

		OnSelectedCounterChanged?.Invoke(this, new OnSelectedCounterChangedEventArgs
		{
			selectedCounter = selectedCounter
		});
	}

	public Transform GetKitchenObjectFollowTransform() { return kitchenObjectHoldPoint; }

	public void SetKitchenObject(KitchenObject kitchenObject)
	{
		this.kitchenObject = kitchenObject;
		if (kitchenObject)
		{
			OnPickedSomething?.Invoke(this, EventArgs.Empty);
			OnAnyPlayerPickedSomething?.Invoke(this, EventArgs.Empty);
		}
	}

	public KitchenObject GetKitchenObject() => kitchenObject;

	public void ClearKitchenObject() { kitchenObject = null; }

	public bool HasKitchenObject() { return kitchenObject; }

	public NetworkObject GetNetworkObject() { return NetworkObject; }

	public static void ResetStaticData()
	{
		OnAnyPlayerSpawned = null;
		OnAnyPlayerPickedSomething = null;
	}
}
