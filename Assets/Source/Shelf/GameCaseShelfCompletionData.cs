using UnityEngine;

namespace EXW.Multiplayer
{
	/// <summary>
	/// Immutable local payload raised when one logical shelf slot becomes
	/// complete. The same semantic event is raised independently on every
	/// peer after the replicated slot snapshot arrives.
	/// </summary>
	public readonly struct GameCaseShelfCompletionData
	{
		public GameCaseShelfCompletionData(
			NetworkGameCaseShelfSlotState slotState,
			Vector3 worldPosition,
			Quaternion worldRotation,
			uint appId,
			string gameName,
			uint playtimeMinutes,
			int caseCount,
			long rewardAmount,
			uint revision,
			bool isServer)
		{
			SlotState = slotState;
			WorldPosition = worldPosition;
			WorldRotation = worldRotation;
			AppId = appId;
			GameName = gameName ?? string.Empty;
			PlaytimeMinutes = playtimeMinutes;
			CaseCount = caseCount;
			RewardAmount = rewardAmount;
			Revision = revision;
			IsServer = isServer;
		}

		public NetworkGameCaseShelfSlotState SlotState { get; }
		public Vector3 WorldPosition { get; }
		public Quaternion WorldRotation { get; }
		public uint AppId { get; }
		public string GameName { get; }
		public uint PlaytimeMinutes { get; }
		public float PlaytimeHours => PlaytimeMinutes / 60f;
		public int CaseCount { get; }
		public long RewardAmount { get; }
		public uint Revision { get; }
		public bool IsServer { get; }
	}
}