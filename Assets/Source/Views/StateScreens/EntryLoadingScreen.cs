using MEC;
using UnityEngine;
using UnityEngine.SceneManagement;

public class EntryLoadingScreen : MonoBehaviour
{
	private void Start()
	{
		Timing.CallDelayed(Random.Range(0.01f, 0.05f), () =>
		{
			int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
			SceneManager.LoadScene(currentSceneIndex + 1);
		});
	}
}