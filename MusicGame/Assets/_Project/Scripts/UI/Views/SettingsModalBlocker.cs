using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>The settings card owns a full-screen sibling veil, independent of its own small bounds.</summary>
public sealed class SettingsModalBlocker : MonoBehaviour
{
    public Image backdrop;
    public Selectable initialSelection;
    GameObject previousSelection;
    void OnEnable()=>ShowVeil();
    void OnDisable()=>HideVeil();
    public void ShowVeil()
    {
        if(backdrop==null)return;
        backdrop.raycastTarget=true;backdrop.gameObject.SetActive(true);
        backdrop.transform.SetAsLastSibling();transform.SetAsLastSibling();
        backdrop.GetComponent<ModalVeilTone>()?.Apply();
        if(EventSystem.current!=null)
        {
            previousSelection=EventSystem.current.currentSelectedGameObject;
            if(initialSelection!=null)EventSystem.current.SetSelectedGameObject(initialSelection.gameObject);
        }
    }
    public void HideVeil()
    {
        if(backdrop!=null)backdrop.gameObject.SetActive(false);
        if(EventSystem.current!=null&&previousSelection!=null&&previousSelection.activeInHierarchy)
            EventSystem.current.SetSelectedGameObject(previousSelection);
        previousSelection=null;
    }
}
