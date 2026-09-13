using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
	[DisallowMultipleComponent]
	[RequireComponent(typeof(NetworkObject))]
	[RequireComponent(typeof(BoxCollider))]
	public sealed class NetworkClickableCube : NetworkBehaviour
	{
		private NetworkCubeRoundController roundController;
		private bool consumedOnServer;

		public void InitializeOnServer(
			NetworkCubeRoundController controller)
		{
			if (!IsServer && NetworkObject.IsSpawned)
			{
				Debug.LogError(
					"[CubeTest] Cube can only be initialized by the server.",
					this);

				return;
			}

			roundController = controller;
		}

		public void RequestConsume()
		{
			if (!IsSpawned || !IsClient)
				return;

			RequestConsumeRpc();
		}

		[Rpc(SendTo.Server, RequireOwnership = false)]
		private void RequestConsumeRpc(
			RpcParams rpcParams = default)
		{
			if (!IsServer || consumedOnServer)
				return;

			if (roundController == null)
			{
				Debug.LogError(
					"[CubeTest] Cube has no round controller on the server.",
					this);

				return;
			}

			ulong senderClientId =
				rpcParams.Receive.SenderClientId;

			if (!roundController.TryConsumeCube(
				this,
				senderClientId))
			{
				return;
			}

			consumedOnServer = true;

			Debug.Log(
				$"[CubeTest] Cube consumed by ClientId={senderClientId}.",
				this);

			NetworkObject.Despawn(true);
		}
	}
}