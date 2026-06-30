using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class RingController : MonoBehaviour
{
    public RingType Type { get; private set; }

    private bool    _collected;
    private Vector3 _originalScale;

    public void Setup(RingType type)
    {
        Type            = type;
        _originalScale  = transform.localScale;
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnEnable()
    {
        // Reset state when reactivated (defensive — rings are activated once per game)
        _collected = false;
        if (_originalScale != Vector3.zero)
            transform.localScale = _originalScale;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_collected) return;
        if (other.GetComponent<PlayerController>() == null) return;

        _collected = true;
        EventBus.Publish(new RingCollectedEvent { Type = Type, Position = transform.position });
        StartCoroutine(CollectAnimation());
    }

    private IEnumerator CollectAnimation()
    {
        float elapsed  = 0f;
        float duration = 0.14f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            transform.localScale = Vector3.Lerp(_originalScale, Vector3.zero, t * t);
            yield return null;
        }

        // Reset scale before deactivating so it's ready if reactivated
        transform.localScale = _originalScale;
        gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!_collected)
            transform.Rotate(0f, 55f * Time.deltaTime, 0f);
    }
}
