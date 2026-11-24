using System;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class JukeboxTimeline : Slider
{
    private int? _pointerId;
    public bool Scrubbing => _pointerId.HasValue;
    public event Action<float> SeekRequested;

    protected override void Awake()
    {
        base.Awake();
        onValueChanged.AddListener(SeekWithoutPointer);
    }

    public override void OnPointerDown(PointerEventData eventData)
    {
        if (Scrubbing || !IsActive() || !IsInteractable() ||
            eventData.button != PointerEventData.InputButton.Left) return;
        _pointerId = eventData.pointerId;
        base.OnPointerDown(eventData);
    }

    public override void OnDrag(PointerEventData eventData)
    {
        if (_pointerId == eventData.pointerId) base.OnDrag(eventData);
    }

    public override void OnPointerUp(PointerEventData eventData)
    {
        if (_pointerId != eventData.pointerId) return;
        base.OnPointerUp(eventData);
        CancelScrub();
        if (IsActive() && IsInteractable()) SeekRequested?.Invoke(value);
    }

    public void CancelScrub() => _pointerId = null;

    protected override void OnDisable()
    {
        CancelScrub();
        base.OnDisable();
    }

    private void SeekWithoutPointer(float fraction)
    {
        if (!Scrubbing) SeekRequested?.Invoke(fraction);
    }
}
