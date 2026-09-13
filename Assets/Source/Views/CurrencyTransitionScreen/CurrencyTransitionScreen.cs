using System.Collections;
using UnityEngine;
using DG.Tweening;
using Random = UnityEngine.Random;
using Sequence = DG.Tweening.Sequence;
using System;
using Sirenix.OdinInspector;

public class CurrencyTransitionScreen : ControllerBaseModel
{
	[SerializeField] private PoolModel imagePool;
	private Action OnParticleCollected;
	private float _playDuration = 0.15f;
	private float _emissionsPerSecond = 10f;
	public Vector2 emissionDirection = new Vector2(0, 1f);
	public float emissionAngle = 360f; // Random Range where particles are emitted

	[Header("Spawn and move variables")] 
	public Transform targetTransform; //RECT TRANSFORM WILL DO FINE
	public float targetBumpAmount = .2f;
	public float endScale = .8f;
	public float maxScale = 1.6f;
	public float minRadius = 15f;
	public float maxRadius = 150f;
	public Ease spawnEase = Ease.OutQuint;
	public float spawnTime = 1f;
	public float waitTime = .3f;
	public Ease moveEase = Ease.InQuad;
	public float moveTime = 1f;
	public float scaleUpDelay = .2f;
	public float scaleUpTime = .7f;
	public float scaleDownDelay = .4f;
	public float scaleDownTime = .6f;

	private void Emit(Vector3 spawnPos, int increaseAmount)
	{
		StartCoroutine(EmitRoutine(targetTransform, spawnPos, increaseAmount));
	}

	private IEnumerator EmitRoutine(Transform target, Vector3 spawnPos, int increaseAmount)
	{
		float playtime = 0f;
		float emissionPerSecond = _emissionsPerSecond;
		float duration = _playDuration;
		var particleTimer = 0f;
		Vector3 targetScale = targetTransform.localScale;

		while (playtime < duration)
		{
			playtime += Time.deltaTime;
			particleTimer += Time.deltaTime;

			while (particleTimer > 1f / emissionPerSecond)
			{
				particleTimer -= 1f / emissionPerSecond;
				CurrencyEffectModel currency = imagePool.GetDeactiveItem<CurrencyEffectModel>();
				SetParticleSequence(currency, target, spawnPos, increaseAmount);
			}

			yield return new WaitForEndOfFrame();
		}

		DOVirtual.DelayedCall(spawnTime + waitTime + moveTime + .21f, () => targetTransform.DOScale(targetScale, .1f));
	}

	private void SetParticleSequence(CurrencyEffectModel particle, Transform target, Vector3 spawnPos,
		int increaseAmount)
	{
		Vector3 randomDirection =
			Quaternion.AngleAxis(Random.Range(-emissionAngle / 2f, emissionAngle / 2f), Vector3.forward) *
			emissionDirection;
		randomDirection = randomDirection.normalized * Random.Range(minRadius, maxRadius);

		particle.OnSpawn(spawnPos + randomDirection * .3f, .1f * Vector3.one);

		Sequence particleSequence = DOTween.Sequence();

		particleSequence.Insert(0, particle.transform.DOMove(spawnPos + randomDirection, spawnTime).SetEase(spawnEase))
			.Insert(spawnTime + waitTime, particle.transform.DOMove(target.position, moveTime).SetEase(moveEase))
			.Insert(scaleUpDelay, particle.transform.DOScale(maxScale, scaleUpTime))
			.Insert(spawnTime + waitTime + scaleDownDelay,
				particle.transform.DOScale(Vector3.one * endScale, scaleDownTime));

		DOVirtual.DelayedCall(particleSequence.Duration(), () => particle.OnDeactive());
		DOVirtual.DelayedCall(particleSequence.Duration(),
			() => targetTransform.DOPunchScale(
				Vector3.one * Mathf.Clamp(targetBumpAmount, 0,
					1 + targetBumpAmount + .1f - targetTransform.localScale.x), .2f, 1, 0));
		DOVirtual.DelayedCall(particleSequence.Duration(), OnComplete);
	}

	private void OnComplete()
	{
		EventController.Invoke_OnCoinUpdated();
	}

	private void EmitParticlesInTimePosition(int count, float t, Vector3 spawnPos, int increaseAmount)
	{
		_emissionsPerSecond = count / t;
		_playDuration = t;
		Emit(spawnPos, increaseAmount);
	}

	public void EmitParticlesInTimeTransform(int count, float t, Transform spawnTransform, int increaseAmount)
	{
		EmitParticlesInTimePosition(count, t, spawnTransform.position, increaseAmount);
	}

	[Button]
	public void EmitInPosition(int increaseAmount, Vector3 position, int moneyAmount = 1)
	{
		Vector3 screenPos = CameraController.main.WorldToScreenPoint(position);
		EmitParticlesInTimePosition(count: moneyAmount, 0.1f, screenPos, increaseAmount: increaseAmount);
	}

	private void OnEnable()
	{
		
	}

	private void OnDisable()
	{
		
	}
}