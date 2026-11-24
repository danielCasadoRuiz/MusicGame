using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(ScrollRect))]
public sealed class JukeboxCarousel : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
{
    public const int SlotCount = 7;
    private const int Middle = SlotCount / 2;
    private const float Pitch = 110f;
    private const float ScrollSpeed = 8f;

    private JukeboxRowView[] _slots = Array.Empty<JukeboxRowView>();
    private IReadOnlyList<JukeboxTrack> _tracks = Array.Empty<JukeboxTrack>();
    private Action<int> _selected, _clicked;
    private float _position;
    private float _dragOrigin, _dragSampleTime, _velocity;
    private const float InertiaDecay = 6f;
    private const float MinGlideSpeed = 1.6f;
    private int _target;
    private int _reported = -1;
    private bool _dragging;
    private bool _playOnArrival;

    // Keep short lists bounded, so bringing an item around an edge never shows it twice.
    private bool Wraps => _tracks.Count >= SlotCount;
    private int Center => Mathf.FloorToInt(_position + .5f);

    public void Setup(JukeboxRowView[] slots, IReadOnlyList<JukeboxTrack> tracks,
        Action<int> selected, Action<int> clicked)
    {
        _slots = slots;
        _tracks = tracks;
        _selected = selected;
        _clicked = clicked;
        for (int i = 0; i < _slots.Length; i++)
        {
            int relative = i - Middle;
            _slots[i].button.onClick.AddListener(() => CenterClickedRow(relative));
        }
        Focus(tracks.Count == 0 ? -1 : 0);
    }

    public void Focus(int index)
    {
        _position = _target = Mathf.Max(0, index);
        _reported = index;
        _velocity = 0;
        _dragging = _playOnArrival = false;
        Paint();
    }

    public static int Wrap(int index, int count) => count > 0 ? (index % count + count) % count : -1;

    private int IndexFor(int position) =>
        Wraps ? Wrap(position, _tracks.Count) : position >= 0 && position < _tracks.Count ? position : -1;

    private int Limit(int position) => Wraps ? position : Mathf.Clamp(position, 0, _tracks.Count - 1);

    private void CenterClickedRow(int relative)
    {
        if (_dragging) return;
        int destination = Center + relative;
        if (IndexFor(destination) < 0) return;
        _velocity = 0;
        _target = destination;
        _playOnArrival = true;
        if (Mathf.Approximately(_position, _target)) FinishClick();
    }

    private void Paint()
    {
        int center = Center;
        float width = ((RectTransform)transform).rect.width;
        for (int i = 0; i < _slots.Length; i++)
        {
            var row = _slots[i];
            int slot = center + i - Middle;
            int index = IndexFor(slot);
            float position = slot - _position;
            bool centered = index >= 0 && slot == center;
            var rt = (RectTransform)row.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
            rt.anchoredPosition = new Vector2(0, -position * Pitch);
            if (width > 0) rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            float focus = centered ? 1 - Mathf.Clamp01(Mathf.Abs(position) * 2) : 0;
            rt.localScale = Vector3.one * (1 + .035f * focus);
            row.title.text = index >= 0 ? _tracks[index].title : "";
            row.button.interactable = index >= 0;
            row.SetSelected(centered, index >= 0);
        }
    }

    private void MoveTo(float position)
    {
        _position = position;
        Paint();
        if (!_playOnArrival) ReportCenter();
    }

    private void ReportCenter()
    {
        int index = IndexFor(Center);
        if (index == _reported || index < 0) return;
        _reported = index;
        _selected?.Invoke(index);
    }

    private void FinishClick()
    {
        _playOnArrival = false;
        _reported = IndexFor(_target);
        _clicked?.Invoke(_reported);
    }

    private void LateUpdate()
    {
        if (_dragging) return;
        if (Mathf.Abs(_velocity) >= MinGlideSpeed)
        {
            float speed = _velocity * Mathf.Exp(-InertiaDecay * Time.unscaledDeltaTime);
            float next = _position + (_velocity - speed) / InertiaDecay;
            float bounded = Bound(next);
            MoveTo(bounded);
            _velocity = Mathf.Approximately(bounded, next) ? speed : 0;
            _target = Center;
            if (Mathf.Abs(_velocity) >= MinGlideSpeed) return;
            _velocity = 0;
        }
        if (Mathf.Approximately(_position, _target)) return;
        MoveTo(Mathf.MoveTowards(_position, _target, ScrollSpeed * Time.unscaledDeltaTime));
        if (_playOnArrival && Mathf.Approximately(_position, _target)) FinishClick();
    }

    public void OnScroll(PointerEventData eventData)
    {
        if (_tracks.Count < 2 || _dragging || Mathf.Abs(eventData.scrollDelta.y) < .01f) return;
        int direction = eventData.scrollDelta.y < 0 ? 1 : -1;
        _playOnArrival = false;
        _velocity = 0;
        _target = Limit(_target + direction);
        if (Mathf.Approximately(_position, _target)) ReportCenter();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (_tracks.Count < 2) return;
        _dragOrigin = _position;
        _dragSampleTime = Time.unscaledTime;
        _velocity = 0;
        _playOnArrival = false;
        _dragging = true;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_dragging) return;
        var rect = (RectTransform)transform;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, eventData.pressEventCamera, out var now) ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.pressPosition, eventData.pressEventCamera, out var start)) return;
        float position = Bound(_dragOrigin + (now.y - start.y) / Pitch);
        float time = Time.unscaledTime;
        float delta = Mathf.Max(time - _dragSampleTime, .015f);
        _velocity = Mathf.Clamp((position - _position) / delta, -ScrollSpeed, ScrollSpeed);
        _dragSampleTime = time;
        MoveTo(position);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!_dragging) return;
        _dragging = false;
        if (Time.unscaledTime - _dragSampleTime > .1f || Mathf.Abs(_velocity) < MinGlideSpeed) _velocity = 0;
        _target = Center;
        if (Mathf.Approximately(_position, _target)) ReportCenter();
    }

    private float Bound(float position) => Wraps ? position : Mathf.Clamp(position, 0, _tracks.Count - 1);

    private void OnDisable()
    {
        _dragging = _playOnArrival = false;
        _velocity = 0;
        _position = _target = Center;
    }
}
