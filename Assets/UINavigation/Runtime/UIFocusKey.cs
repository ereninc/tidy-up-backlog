using UnityEngine;

namespace EXW.UI.Navigation
{
	/// <summary>An optional stable ID for generated entries whose GameObjects may be rebuilt.</summary>
	[DisallowMultipleComponent]
	public sealed class UIFocusKey : MonoBehaviour
	{
		[SerializeField] private string key;
		public string Key => key;
		public void SetKey(string value) => key = value;
	}
}