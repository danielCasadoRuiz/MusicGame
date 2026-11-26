using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>The settings card owns a full-screen sibling veil, independent of its own small bounds.</summary>
public sealed class SettingsModalBlocker : MonoBehaviour
{
    public Image backdrop;
    public Selectable initialSelection;
    public CanvasGroup menuPresentation;
    float priorAlpha=1;
    bool veilShown;
    GameObject previousSelection;
    void OnEnable()=>ShowVeil();
    void OnDisable()=>HideVeil();
    public void ShowVeil()
    {
        if(backdrop==null)return;
        if(menuPresentation==null)
        {
            var menu=GetComponentInParent<MainMenuView>();
            if(menu!=null&&menu.presentationRoot!=null){menuPresentation=menu.presentationRoot.GetComponent<CanvasGroup>();if(menuPresentation==null)menuPresentation=menu.presentationRoot.gameObject.AddComponent<CanvasGroup>();}
        }
        if(menuPresentation!=null&&!veilShown){priorAlpha=menuPresentation.alpha;menuPresentation.alpha=1f;}
        veilShown=true;
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
        if(menuPresentation!=null&&veilShown)menuPresentation.alpha=priorAlpha;
        veilShown=false;
        if(backdrop!=null)backdrop.gameObject.SetActive(false);
        if(EventSystem.current!=null&&previousSelection!=null&&previousSelection.activeInHierarchy)
            EventSystem.current.SetSelectedGameObject(previousSelection);
        previousSelection=null;
    }
}
