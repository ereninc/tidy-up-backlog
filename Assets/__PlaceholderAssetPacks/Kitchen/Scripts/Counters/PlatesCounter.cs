using System;
using Unity.Netcode;
using UnityEngine;

public class PlatesCounter : BaseCounter
{
	public event EventHandler OnPlateSpawned;
	public event EventHandler OnPlateRemoved;

	[SerializeField] private KitchenObjectSO plateKitchenObjectSO;

	private float spawnPlateTimer;
	private float spawnPlateTimerMax = 4f;
	private int platesSpawnedAmount;
	private int platesSpawnedAmountMax = 4;

	private void Update()
	{
		if (!IsServer) return;

		spawnPlateTimer += Time.deltaTime;
		if (spawnPlateTimer > spawnPlateTimerMax)
		{
			spawnPlateTimer = 0f;

			if (KitchenGameManager.Instance.IsGamePlaying() && platesSpawnedAmount < platesSpawnedAmountMax)
			{
				SpawnPlateServerRpc();
			}
		}
	}

	#region [ SPAWN RPC ]
	[ServerRpc]
	private void SpawnPlateServerRpc() { SpawnPlateClientRpc(); }

	[ClientRpc]
	private void SpawnPlateClientRpc()
	{
		platesSpawnedAmount++;
		OnPlateSpawned?.Invoke(this, EventArgs.Empty);
	}
	#endregion

	public override void Interact(Player player)
	{
		if (!player.HasKitchenObject())
		{
			// Player is empty handed
			if (platesSpawnedAmount > 0)
			{
				// There's at least one plate here
				KitchenObject.SpawnKitchenObject(plateKitchenObjectSO, player);
				PickupPlateServerRpc();
			}
		}
	}

	#region [ INTERACTION RPC ]
	[ServerRpc(RequireOwnership = false)]
	private void PickupPlateServerRpc() { PickupPlateClientRpc(); }

	[ClientRpc]
	private void PickupPlateClientRpc()
	{
		platesSpawnedAmount--;
		OnPlateRemoved?.Invoke(this, EventArgs.Empty);
	}
	#endregion
}
