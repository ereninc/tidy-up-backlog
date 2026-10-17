using EXW.Multiplayer;
using Sirenix.OdinInspector.Editor;
using UnityEditor;

[CustomEditor(typeof(ComputerLocalPresenter)), CanEditMultipleObjects]
internal sealed class ComputerLocalPresenterEditor : OdinEditor { }

[CustomEditor(typeof(StoreComputerDesktopPresenter)), CanEditMultipleObjects]
internal sealed class StoreComputerDesktopPresenterEditor : OdinEditor { }
