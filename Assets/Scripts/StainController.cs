using UnityEngine;

public enum StainState { Whole, PartiallyErased, FullyErased }

[RequireComponent(typeof(Renderer))]
[RequireComponent(typeof(MeshFilter))]
public class StainController : MonoBehaviour
{
    [Header("Пороги состояний")]
    [Range(0f, 1f)] public float partialThreshold = 0.3f;
    [Range(0f, 1f)] public float fullThreshold = 0.9f;

    [Header("Маска")]
    public int maskResolution = 256;
    [Range(0f, 1f)] public float maskCutoff = 0.3f;
    public float recalcInterval = 0.1f;

    [Header("Отладка")]
    public bool logStateChanges = true;
    public bool logDiagnostics = false;

    public event System.Action<StainState> OnStateChanged;

    public StainState CurrentState { get; private set; } = StainState.Whole;
    public float ErasedRatio { get; private set; }

    // Двойная буферизация RT
    RenderTexture _maskRead;
    RenderTexture _maskWrite;

    Material _material;
    Renderer _renderer;
    MeshFilter _meshFilter;

    Material _blitMat;
    Texture2D _brushTex;

    // Геометрия пятна
    Bounds _localBounds;
    float _planeAxis; // 0 = XY, 1 = XZ

    float _recalcTimer;

    // ---------------- Жизненный цикл ----------------

    void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _meshFilter = GetComponent<MeshFilter>();

        if (_renderer == null || _meshFilter == null || _meshFilter.sharedMesh == null)
        {
            Debug.LogError("[StainController] Нужны Renderer и MeshFilter с мешем.", this);
            enabled = false;
            return;
        }

        _material = _renderer.material;

        // Границы и определение плоскости
        _localBounds = _meshFilter.sharedMesh.bounds;
        float maxXY = Mathf.Max(_localBounds.size.x, _localBounds.size.y);
        _planeAxis = (_localBounds.size.z < maxXY * 0.01f) ? 0f : 1f;

        _material.SetVector("_BoundsMin",
            new Vector4(_localBounds.min.x, _localBounds.min.y, _localBounds.min.z, 0f));
        _material.SetVector("_BoundsSize",
            new Vector4(_localBounds.size.x, _localBounds.size.y, _localBounds.size.z, 0f));
        _material.SetFloat("_PlaneAxis", _planeAxis);

        if (logDiagnostics)
            Debug.Log($"[StainController] bounds.min={_localBounds.min}, size={_localBounds.size}, " +
                      $"planeAxis={_planeAxis} (0=XY, 1=XZ)", this);

        // Две RT для двойной буферизации
        _maskRead = CreateMaskRT();
        _maskWrite = CreateMaskRT();
        ClearRT(_maskRead);
        ClearRT(_maskWrite);

        _material.SetTexture("_EraseMask", _maskRead);

        // Blit-материал
        var shader = Shader.Find("Hidden/StainBrushURP");
        if (shader == null)
        {
            Debug.LogError("[StainController] Шейдер 'Hidden/StainBrushURP' не найден.", this);
            enabled = false;
            return;
        }
        _blitMat = new Material(shader);
        _brushTex = CreateBrushTexture(64);
    }

    void OnDestroy()
    {
        if (_maskRead != null) { _maskRead.Release(); Destroy(_maskRead); }
        if (_maskWrite != null) { _maskWrite.Release(); Destroy(_maskWrite); }
        if (_blitMat != null) Destroy(_blitMat);
        if (_brushTex != null) Destroy(_brushTex);
        if (_material != null) Destroy(_material);
    }

    void Update()
    {
        _recalcTimer -= Time.deltaTime;
        if (_recalcTimer <= 0f)
        {
            _recalcTimer = recalcInterval;
            RecalculateErasedRatio();
            UpdateState();
        }
    }

    // ---------------- Публичное API ----------------

    /// <summary>
    /// Стереть часть маски вокруг мировой точки.
    /// </summary>
    public void EraseAt(Vector3 worldPos, float radius, float strength)
    {
        if (_blitMat == null || _maskRead == null || _maskWrite == null) return;

        float worldSize = GetWorldSize();
        if (worldSize <= 1e-5f) return;

        Vector2 uv = WorldToMaskUV(worldPos);

        _blitMat.SetTexture("_BrushTex", _brushTex);
        _blitMat.SetVector("_BrushPos", new Vector4(uv.x, uv.y, 0f, 0f));
        _blitMat.SetFloat("_BrushRadius", radius / worldSize);
        _blitMat.SetFloat("_BrushStrength", Mathf.Clamp01(strength));

        // Graphics.Blit автоматически устанавливает _MainTex = source
        Graphics.Blit(_maskRead, _maskWrite, _blitMat);

        // Меняем буферы местами
        (_maskRead, _maskWrite) = (_maskWrite, _maskRead);

        // Обновляем текстуру в материале пятна
        _material.SetTexture("_EraseMask", _maskRead);

        // Форсируем пересчёт в ближайшем Update
        _recalcTimer = 0f;
    }

    public void ResetStain()
    {
        if (_maskRead == null) return;
        ClearRT(_maskRead);
        ClearRT(_maskWrite);
        _material.SetTexture("_EraseMask", _maskRead);
        ErasedRatio = 0f;
        CurrentState = StainState.Whole;
        OnStateChanged?.Invoke(CurrentState);
    }

    // ---------------- Внутреннее ----------------

    RenderTexture CreateMaskRT()
    {
        var rt = new RenderTexture(maskResolution, maskResolution, 0, RenderTextureFormat.ARGB32)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            useMipMap = false,
            autoGenerateMips = false
        };
        rt.Create();
        return rt;
    }

    void ClearRT(RenderTexture rt)
    {
        var prev = RenderTexture.active;
        Graphics.SetRenderTarget(rt);
        GL.Clear(true, true, Color.black);
        Graphics.SetRenderTarget(prev);
    }

    void RecalculateErasedRatio()
    {
        var prev = RenderTexture.active;
        RenderTexture.active = _maskRead;

        var tex = new Texture2D(maskResolution, maskResolution, TextureFormat.RGBA32, false, true);
        tex.ReadPixels(new Rect(0, 0, maskResolution, maskResolution), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;

        var pixels = tex.GetRawTextureData<byte>();
        int total = maskResolution * maskResolution;
        int erased = 0;
        int nonZero = 0;
        byte maxByte = 0;

        for (int i = 0; i < total; i++)
        {
            byte b = pixels[i * 4]; // R-канал
            if (b > maxByte) maxByte = b;
            if (b > 0) nonZero++;
            if (b / 255f >= maskCutoff) erased++;
        }

        ErasedRatio = (float)erased / total;

        if (logDiagnostics)
            Debug.Log($"[StainController] maxMask={maxByte}/255, nonZero={nonZero}/{total}, " +
                      $"erased={erased}/{total} = {ErasedRatio:P2}", this);

        Destroy(tex);
    }

    void UpdateState()
    {
        StainState newState;
        if (ErasedRatio >= fullThreshold) newState = StainState.FullyErased;
        else if (ErasedRatio >= partialThreshold) newState = StainState.PartiallyErased;
        else newState = StainState.Whole;

        if (newState != CurrentState)
        {
            CurrentState = newState;
            if (logStateChanges)
                Debug.Log($"[StainController] {name}: {newState} (erased={ErasedRatio:P1})", this);
            OnStateChanged?.Invoke(CurrentState);
            ApplyStateVisuals();
        }
    }

    void ApplyStateVisuals()
    {
        switch (CurrentState)
        {
            case StainState.Whole:
                break;
            case StainState.PartiallyErased:
                break;
            case StainState.FullyErased:
                // Например:
                // gameObject.SetActive(false);
                break;
        }
    }

    Vector2 WorldToMaskUV(Vector3 worldPos)
    {
        Vector3 local = transform.InverseTransformPoint(worldPos);
        float u, v;
        if (_planeAxis < 0.5f)
        {
            u = Mathf.InverseLerp(_localBounds.min.x, _localBounds.max.x, local.x);
            v = Mathf.InverseLerp(_localBounds.min.y, _localBounds.max.y, local.y);
        }
        else
        {
            u = Mathf.InverseLerp(_localBounds.min.x, _localBounds.max.x, local.x);
            v = Mathf.InverseLerp(_localBounds.min.z, _localBounds.max.z, local.z);
        }
        return new Vector2(u, v);
    }

    float GetWorldSize()
    {
        float localSize, scale;
        if (_planeAxis < 0.5f)
        {
            localSize = Mathf.Max(_localBounds.size.x, _localBounds.size.y);
            scale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
        }
        else
        {
            localSize = Mathf.Max(_localBounds.size.x, _localBounds.size.z);
            scale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
        }
        return localSize * scale;
    }

    Texture2D CreateBrushTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
        var px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float v = Mathf.Clamp01(1f - d / c);
                byte b = (byte)(v * v * 255f); // мягкий край
                px[y * size + x] = new Color32(b, b, b, 255);
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        return tex;
    }

    void OnDrawGizmosSelected()
    {
        if (_meshFilter == null || _meshFilter.sharedMesh == null) return;
        var b = _meshFilter.sharedMesh.bounds;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(transform.TransformPoint(b.center),
                            Vector3.Scale(b.size, transform.lossyScale));
    }
}