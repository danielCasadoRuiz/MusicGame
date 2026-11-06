using System;
using UnityEngine;

/// <summary>Isolated visual review; uses the game's avatar recipes without starting a run or changing a save.</summary>
public sealed class CombatLookPreviewActors : MonoBehaviour
{
    public AvatarRecipeSO playerRecipe, rivalRecipe;
    public AnimationClip guardClip;
    public Camera previewCamera;
    public ArenaCombatLook look;
    public bool Ready { get; private set; }
    public string Error { get; private set; }
    public bool frozen;
    public AvatarInstance Player { get; private set; }
    public AvatarInstance Rival { get; private set; }
    AvatarClipPlayer playerClip, rivalClip;

    async void Start()
    {
        try
        {
            var a = new GameObject("PreviewPlayer").transform; a.SetParent(transform, false); a.position = new Vector3(-1.05f, 0, 0); a.rotation = Quaternion.Euler(0, 90, 0);
            var b = new GameObject("PreviewMonteverdi").transform; b.SetParent(transform, false); b.position = new Vector3(1.05f, 0, 0); b.rotation = Quaternion.Euler(0, -90, 0);
            Player = await AvatarFactory.CreateAsync(playerRecipe.ToRuntime(), a);
            Rival = await AvatarFactory.CreateAsync(rivalRecipe.ToRuntime(), b);
            if (!this) return;
            playerClip = new AvatarClipPlayer(Player.Animator); playerClip.Play(guardClip, true, 0); playerClip.Seek(.35f);
            rivalClip = new AvatarClipPlayer(Rival.Animator); rivalClip.Play(guardClip, true, 0); rivalClip.Seek(.65f);
            look.RegisterFighterRoot(Player.Root); look.RegisterFighterRoot(Rival.Root); look.AttachCamera(previewCamera); look.SetTreatment(true);
            Ready = true;
        }
        catch (Exception e) { Error = e.ToString(); Debug.LogException(e); }
    }
    void Update() { if (Ready && !frozen) { playerClip.Tick(Time.deltaTime); rivalClip.Tick(Time.deltaTime); } }
    void OnDestroy() { playerClip?.Dispose(); rivalClip?.Dispose(); Player?.Dispose(); Rival?.Dispose(); }
}
