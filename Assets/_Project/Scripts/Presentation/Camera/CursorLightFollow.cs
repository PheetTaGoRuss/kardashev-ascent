using UnityEngine;

public class CursorLightFollow : MonoBehaviour
{
    public Camera mainCamera;
    public LayerMask groundLayer;
    public float heightOffset = 1.0f;

    private Light cursorLight;

    void Start()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                Debug.LogError("CursorLightFollow: No Main Camera found! Make sure your camera is tagged as 'MainCamera'.");
            }
        }

        cursorLight = GetComponent<Light>();
        if (cursorLight == null)
        {
            Debug.LogError("CursorLightFollow: No Light component found on this GameObject!");
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F))
        {
            if (cursorLight != null)
            {
                cursorLight.enabled = !cursorLight.enabled;
            }
        }

        if (cursorLight != null && !cursorLight.enabled) return;
        if (mainCamera == null) return;

        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        int layerMask = groundLayer == 0 ? ~0 : groundLayer.value;

        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, layerMask))
        {
            transform.position = hit.point + (hit.normal * heightOffset);
        }
    }
}