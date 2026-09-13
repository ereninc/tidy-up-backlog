using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GraphicSettingSaveController : MonoBehaviour
{
	// protected override PrefType GetPrefType() => PrefType.GraphicSetting;
	public static new GraphicSettingSaveController Instance => Singleton<GraphicSettingSaveController>.Instance;

	private bool isLoading = true;
	
	public void SetGraphicSettings(int resolutionIndex, int screenIndex, int qualityIndex)
	{
		if (isLoading) return;  
		Debug.Log($"[Save] Saving Graphic Settings - Resolution: {resolutionIndex}, ScreenMode: {screenIndex}, Quality: {qualityIndex}");
		
		// Data.ResolutionIndex = resolutionIndex;
		// Data.ScreenModeIndex = screenIndex;
		// Data.QualityIndex = qualityIndex;
		// Save();
	}
	
	public void FinishLoading()
	{
		isLoading = false;
	}
}

[System.Serializable]
public class GraphicSettingData
{
	public int ResolutionIndex; //AUTO
	public int ScreenModeIndex = 0; //FULLSCREEN
	public int QualityIndex = 0; //HIGH
}