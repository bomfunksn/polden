// Parallax.cs — повесить на группу слоя фона (BG_Sky, BG_Forest…) или переднего плана (FG_...).
// Factor: 1 = слой "приклеен" к камере (очень далеко), 0 = обычный объект мира,
//         0.95 небо, 0.8 лес, 0.6 дальнее поле, 0.4 дерево,
//         отрицательное (-0.3) = передний план, движется быстрее камеры.
// Положить в Assets/Scripts/ (НЕ в Editor).

using UnityEngine;

public class Parallax : MonoBehaviour
{
    [Range(-1f, 1f)] public float factor = 0.5f;
    [Tooltip("Параллакс по вертикали. Обычно меньше горизонтального или 0.")]
    [Range(-1f, 1f)] public float verticalFactor = 0.2f;

    Transform cam;
    Vector3 startPos, camStart;

    void Start()
    {
        cam = Camera.main != null ? Camera.main.transform : null;
        startPos = transform.position;
        if (cam != null) camStart = cam.position;
    }

    void LateUpdate()
    {
        if (cam == null) return;
        Vector3 d = cam.position - camStart;
        transform.position = startPos + new Vector3(d.x * factor, d.y * verticalFactor, 0f);
    }
}
