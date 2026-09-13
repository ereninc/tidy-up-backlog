using UnityEngine;

namespace EXW.Multiplayer
{
	[DisallowMultipleComponent]
	[AddComponentMenu(
		"Multiplayer/Game Cases/Shelf/Game Case Shelf Ghost Focus Signal")]
	public sealed class GameCaseShelfGhostFocusSignal : MonoBehaviour
	{
		private NetworkGameCaseShelfDestination _destination;

		private void OnEnable()
		{
			// Her açılışta kendi parent slotunu bulur.
			// Duplicate edilmiş eski serialized referansları kullanmaz.
			_destination =
				GetComponentInParent<NetworkGameCaseShelfDestination>(true);

			if (_destination == null)
			{
				Debug.LogError(
					"[GameCaseShelf] GhostFocusSignal kendi parent'ında " +
					"NetworkGameCaseShelfDestination bulamadı.",
					this);
				return;
			}

			GameCaseShelfGhostPresenter.Instance?.Focus(_destination);
		}

		private void OnDisable()
		{
			if (_destination != null)
			{
				GameCaseShelfGhostPresenter.Instance?.ClearFocus(_destination);
			}

			_destination = null;
		}
	}
}