using Unity.Netcode.Components;
using UnityEngine;

namespace EXW.Multiplayer
{
	/// <summary>
	/// Client-owner authoritative transform for responsive co-op character movement.
	/// Gameplay interactions remain server authoritative; this class only controls
	/// who publishes the player's movement transform.
	/// </summary>
	[DisallowMultipleComponent]
	[AddComponentMenu("Multiplayer/Owner Network Transform")]
	public sealed class OwnerNetworkTransform : NetworkTransform
	{
		protected override bool OnIsServerAuthoritative()
		{
			return false;
		}

		private void Reset()
		{
			SyncPositionX = true;
			SyncPositionY = true;
			SyncPositionZ = true;

			SyncRotAngleX = false;
			SyncRotAngleY = true;
			SyncRotAngleZ = false;

			SyncScaleX = false;
			SyncScaleY = false;
			SyncScaleZ = false;

			Interpolate = true;
			InLocalSpace = false;
			UseQuaternionSynchronization = false;
			UseQuaternionCompression = false;
			UseHalfFloatPrecision = true;
			UseUnreliableDeltas = true;
		}
	}
}