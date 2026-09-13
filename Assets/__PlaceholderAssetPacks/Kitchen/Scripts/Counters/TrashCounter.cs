using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class TrashCounter : BaseCounter
{
	public static event EventHandler OnAnyObjectTrashed;

	new public static void ResetStaticData() { OnAnyObjectTrashed = null; }

	public override void Interact(Player player)
	{
		if (player.HasKitchenObject())
		{
			KitchenObject.DespawnKitchenObject(player.GetKitchenObject());
			InteractServerRpc();
		}
	}

	#region [ RPC ]
	[ServerRpc(RequireOwnership = false)]
	private void InteractServerRpc() { InteractClientRpc(); }

	[ClientRpc]
	private void InteractClientRpc() { OnAnyObjectTrashed?.Invoke(this, EventArgs.Empty); }
	#endregion
}
