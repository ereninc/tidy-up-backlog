#if UNITY_EDITOR
using Sirenix.OdinInspector.Editor;
using UnityEditor;

namespace EXW.Multiplayer.Editor
{
	/// <summary>
	/// Forces this component to use Odin even when the project's global inspector
	/// type configuration leaves regular MonoBehaviours on Unity's default editor.
	/// </summary>
	[CustomEditor(typeof(MultiplayerDebugPanel))]
	[CanEditMultipleObjects]
	public sealed class MultiplayerDebugPanelEditor : OdinEditor
	{
	}
}
#endif