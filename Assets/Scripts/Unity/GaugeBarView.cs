using UnityEngine;
using UnityEngine.UI;

public sealed class GaugeBarView : MonoBehaviour
{
    [SerializeField] private Image guFill;
    [SerializeField] private Image chokiFill;
    [SerializeField] private Image paFill;

    [SerializeField] private Image guFrame;
    [SerializeField] private Image chokiFrame;
    [SerializeField] private Image paFrame;

    [SerializeField] private Sprite frameNormal; // 灰色枠
    [SerializeField] private Sprite frameCharged; // 黄色枠

    [SerializeField] private float chargedBlinkSpeed = 2f; // 光り方（任意）

    public void SetGauge(float gu, float choki, float pa, float max)
    {
        SetOne(guFill, guFrame, gu, max);
        SetOne(chokiFill, chokiFrame, choki, max);
        SetOne(paFill, paFrame, pa, max);
    }

    private void SetOne(Image fill, Image frame, float value, float max)
    {
        float t = Mathf.Clamp01(value / max);
        fill.fillAmount = t;

        bool charged = value >= max - 1e-6f;

        if (charged)
        {
            frame.sprite = frameCharged;

            // うっすら光る（α点滅）
            float a = 0.75f + 0.25f * Mathf.PingPong(Time.time * chargedBlinkSpeed, 1f);
            var c = frame.color;
            c.a = a;
            frame.color = c;
        }
        else
        {
            frame.sprite = frameNormal;

            // 通常時は固定表示
            var c = frame.color;
            c.a = 1f;
            frame.color = c;
        }
    }

}
