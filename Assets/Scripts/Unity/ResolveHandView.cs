using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using RpsBuild.Core;

public sealed class ResolveHandsView : MonoBehaviour
{
    [Header("Order matters: 0..6")]
    [SerializeField] private List<Image> enemyIcons = new();
    [SerializeField] private List<Image> playerIcons = new();

    [Header("Sprites")]
    [SerializeField] private Sprite gu;
    [SerializeField] private Sprite choki;
    [SerializeField] private Sprite pa;

    public void Show(IReadOnlyList<RpsColor> enemyPlays, IReadOnlyList<RpsColor> playerPlays)
    {
        Apply(enemyIcons, enemyPlays);
        Apply(playerIcons, playerPlays);
    }

    private void Apply(List<Image> targets, IReadOnlyList<RpsColor> src)
    {
        for (int i = 0; i < targets.Count; i++)
        {
            if (i < src.Count)
            {
                targets[i].gameObject.SetActive(true);
                targets[i].sprite = ToSprite(src[i]);
                targets[i].preserveAspect = true;
            }
            else
            {
                targets[i].gameObject.SetActive(false);
            }
        }
    }

    private Sprite ToSprite(RpsColor c)
    {
        return c switch
        {
            RpsColor.Gu => gu,
            RpsColor.Choki => choki,
            RpsColor.Pa => pa,
            _ => null
        };
    }
}
