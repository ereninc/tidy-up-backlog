using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// public class InputController : Singleton<InputController>, IGameStateObserver
// {
// 	private PlayerInputActions _playerInputActions;
// 	public PlayerInputActions PlayerInputActions => _playerInputActions ??= new PlayerInputActions();
//
// 	public Action GameDeviceChangedAction;
//
// 	public enum GameDevice
// 	{
// 		KeyboardMouse,
// 		Gamepad,
// 	}
//
// 	public GameDevice activeGameDevice;
// 	private Gamepad _gamepad;
//
// 	//TEST STUFF
// 	[SerializeField] private Transform testObject;
// 	[SerializeField] private float moveSpeed = 5f;
//
// 	[SerializeField] private Button inGameMenuButton;
// 	[SerializeField] private bool inGameMenuActive = false;
//
// 	public override void Initialize()
// 	{
// 		base.Initialize();
// 		InputSystem.onActionChange += InputSystem_OnActionChange;
//
// 		PlayerInputActions.Player.ToggleMenu.performed += ToggleMenu;
// 		PlayerInputActions.Player.IngameMenu.performed += ToggleInGameMenu;
//
// 		//TEST OBJECT MOVEMENT
// 		PlayerInputActions.Player.Move.performed += OnMove; // Move TestObject
// 		PlayerInputActions.Player.Move.canceled += OnMoveCancel; // Stop TestObject
// 	}
//
// 	private void Start()
// 	{
// 		AddToGameObserverList();
// 	}
//
// 	private void OnDisable()
// 	{
// 		InputSystem.onActionChange -= InputSystem_OnActionChange;
// 		if (_playerInputActions != null)
// 		{
// 			PlayerInputActions.Player.ToggleMenu.performed -= ToggleMenu;
// 			PlayerInputActions.Player.IngameMenu.performed -= ToggleInGameMenu;
//
// 			//TEST OBJECT MOVEMENT
// 			PlayerInputActions.Player.Move.performed -= OnMove;
// 			PlayerInputActions.Player.Move.canceled -= OnMoveCancel;
// 		}
// 	}
// 	
// 	private void Update()
// 	{
// 		if (GameController.currentGameState != GameStates.Game) return;
//
// 		// If active device is gamepad, use yMovement
// 		float yMovement = 0;
// 		if (_gamepad != null)
// 		{
// 			if (_gamepad.rightTrigger.isPressed) yMovement = 1; // RT -> move up
// 			if (_gamepad.leftTrigger.isPressed) yMovement = -1; // LT -> move down
// 		}
//
// 		Vector3 finalMovement = new Vector3(_moveDirection.x, yMovement, _moveDirection.z);
// 		testObject.position += finalMovement * (moveSpeed * Time.deltaTime);
// 	}
// 	
// 	private void ToggleMenu(InputAction.CallbackContext obj)
// 	{
// 		if (GameController.currentGameState == GameStates.Game)
// 		{
// 			GameController.SetGameState(GameStates.Main);
// 		}
// 		else if (GameController.currentGameState == GameStates.Main)
// 		{
// 			GameController.SetGameState(GameStates.Game);
// 		}
// 	}
// 	
// 	#region [ Test Actions ]
// 	
// 	private void ToggleInGameMenu(InputAction.CallbackContext obj)
// 	{
// 		inGameMenuButton.onClick?.Invoke();
// 	}
//
// 	public void ToggleInGameMenu(bool enableUI)
// 	{
// 		inGameMenuActive = enableUI;
// 		ToggleInputActionMaps(inGameMenuActive);
// 	}
// 	
// 	#endregion
//
// 	#region [ Gamepad Move Actions ]
//
// 	private Vector3 _moveDirection;
//
// 	private void OnMove(InputAction.CallbackContext context)
// 	{
// 		if (GameController.currentGameState != GameStates.Game) return;
//
// 		Vector2 input = context.ReadValue<Vector2>(); // Gamepad Stick Input
// 		_moveDirection = new Vector3(input.x, 0, input.y); // X - Z axis movement
// 	}
//
// 	private void OnMoveCancel(InputAction.CallbackContext context)
// 	{
// 		_moveDirection = Vector3.zero; // Stop movement if no input
// 	}
//
// 	#endregion
//
// 	#region [ Devide Actions ]
// 	
// 	private void InputSystem_OnActionChange(object arg1, InputActionChange inputActionChange)
// 	{
// 		if (inputActionChange == InputActionChange.ActionPerformed && arg1 is InputAction)
// 		{
// 			InputAction inputAction = arg1 as InputAction;
// 			if (inputAction?.activeControl?.device?.displayName == "VirtualMouse") return; //IGNORE VIRTUAL MOUSE
//
// 			if (inputAction.activeControl?.device is Gamepad)
// 			{
// 				if (activeGameDevice != GameDevice.Gamepad)
// 				{
// 					ChangeActiveGameDevice(GameDevice.Gamepad);
// 					_gamepad = Gamepad.all.Count > 0 ? Gamepad.current : null;
// 				}
// 			}
// 			else
// 			{
// 				if (activeGameDevice != GameDevice.KeyboardMouse)
// 				{
// 					ChangeActiveGameDevice(GameDevice.KeyboardMouse);
// 				}
// 			}
// 		}
// 	}
//
// 	private void ChangeActiveGameDevice(GameDevice targetGameDevice)
// 	{
// 		if (activeGameDevice == targetGameDevice) return;
//
// 		activeGameDevice = targetGameDevice;
// 		Debug.Log("New active game device : " + activeGameDevice);
// 		Cursor.visible = activeGameDevice == GameDevice.KeyboardMouse;
// 		GameDeviceChangedAction?.Invoke();
// 	}
//
// 	
//
// 	#endregion
//
// 	#region [ STATE SUBSCRIPTIONS ]
//
// 	public void AddToGameObserverList()
// 	{
// 		GameController.AddListener(this);
// 	}
//
// 	public void OnGameStateChanged()
// 	{
// 		switch (GameController.currentGameState)
// 		{
// 			case GameStates.Main:
// 				ToggleInputActionMaps(true);
// 				break;
// 			case GameStates.Game:
// 				ToggleInputActionMaps(false);
// 				break;
// 			case GameStates.Win:
// 				ToggleInputActionMaps(true);
// 				break;
// 			case GameStates.Lose:
// 				ToggleInputActionMaps(true);
// 				break;
// 		}
// 	}
//
// 	private void ToggleInputActionMaps(bool enableUI)
// 	{
// 		if (enableUI)
// 		{
// 			PlayerInputActions.UI.Enable();
// 			PlayerInputActions.Player.Disable();
// 		}
// 		else
// 		{
// 			PlayerInputActions.UI.Disable();
// 			PlayerInputActions.Player.Enable();
// 		}
// 	}
//
// 	#endregion
// }