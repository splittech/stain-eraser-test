using UnityEngine;

public class StainEraser : MonoBehaviour
{
    [Header("Параметры стирания")]
    [Tooltip("Радиус стирания в мировых единицах")]
    public float eraseRadius = 0.05f;

    [Tooltip("Базовая скорость стирания при радиусе = referenceRadius")]
    public float eraseSpeed = 5f;

    [Tooltip("Эталонный радиус, при котором eraseSpeed используется как есть")]
    public float referenceRadius = 0.3f;

    [Tooltip("Степень компенсации площади: 0 = без, 2 = полная (время стирания не зависит от радиуса)")]
    [Range(0f, 2f)] public float areaCompensation = 2f;

    [Header("Обнаружение")]
    [Tooltip("Слой, на котором лежат пятна")]
    public LayerMask stainLayer = ~0;

    [Tooltip("Максимальная дистанция от центра до пятна")]
    public float maxDistance = 0.6f;

    [Tooltip("Как часто проверять близкие пятна (сек)")]
    public float scanInterval = 0.05f;

    [Header("Отладка")]
    public bool debugLog = false;

    float _scanTimer;

    void Update()
    {
        _scanTimer -= Time.deltaTime;
        if (_scanTimer > 0f) return;
        _scanTimer = scanInterval;

        var hits = Physics.OverlapSphere(
            transform.position, maxDistance, stainLayer, QueryTriggerInteraction.Collide);

        // Компенсация площади: чем меньше кисть, тем сильнее удар
        float r = Mathf.Max(eraseRadius, 0.001f);
        float areaFactor = referenceRadius / r;
        float strength = Mathf.Clamp01(
            eraseSpeed * scanInterval *
            Mathf.Pow(areaFactor, areaCompensation));

        foreach (var h in hits)
        {
            var stain = h.GetComponentInParent<StainController>();
            if (stain == null) continue;

            stain.EraseAt(transform.position, eraseRadius, strength);

            if (debugLog)
                Debug.Log($"[StainEraser] Erase {stain.name} @ {transform.position}, " +
                          $"strength={strength:F3}", stain);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, maxDistance);

        Gizmos.color = new Color(1f, 0f, 0f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, eraseRadius);
    }
}