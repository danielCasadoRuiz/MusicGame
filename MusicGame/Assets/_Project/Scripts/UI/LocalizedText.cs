using TMPro;
using UnityEngine;

/// <summary>
/// Static UI label of a prefab: shows UIText `key` and follows language changes (Loc.Set). Put it on any
/// TextMeshPro whose text never depends on data (titles, column headers, button captions).
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class LocalizedText : MonoBehaviour
{
    public string key;

    private void OnEnable()
    {
        if (!string.IsNullOrEmpty(key)) Loc.Set(GetComponent<TMP_Text>(), key);
    }
}
