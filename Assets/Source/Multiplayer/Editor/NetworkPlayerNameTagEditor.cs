#if UNITY_EDITOR
using Sirenix.OdinInspector.Editor;
using UnityEditor;

namespace EXW.Multiplayer.Editor
{
	/// <summary>
	/// Forces NetworkPlayerNameTag to use Odin when regular MonoBehaviours are
	/// configured to fall back to Unity's default inspector.
	/// </summary>
	[CustomEditor(typeof(NetworkPlayerNameTag))]
	[CanEditMultipleObjects]
	public sealed class NetworkPlayerNameTagEditor : OdinEditor
	{
	}
}
#endif