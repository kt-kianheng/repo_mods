using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>Ember Wand: a fireball grows at the tip during the charge, then flies and explodes (see ChargedWand).</summary>
    public class EmberWand : ChargedWand
    {
        internal static readonly Color Red = new Color(1f, 0.04f, 0.02f);
        internal static readonly Color DarkRed = new Color(0.25f, 0.02f, 0.02f);
        private FireballFx growing;
        private Sound fireSound;   // the patch of fire's crackle ("Burning Fire Loop 1", Floraphonic, Pixabay; as the Ember Staff's inferno)

        protected override Color TipColor => Red;
        protected override Color TipDarkColor => DarkRed;
        protected override float ChargeSeconds => Settings.EmberChargeSeconds.Value;
        protected override string ChargeLines => Settings.EmberChargeLines.Value;
        protected override Vector3 TipLocalPosition => WandBuilder.TipPosition;

        protected override void Awake()
        {
            base.Awake();
            fireSound = MakeSound("inferno_burn.pcm", 0.4f);
        }

        protected override void ChargeBegin(float[] data)
        {
            growing = FireballFx.Create("HWP Fireball (charging)", Tip.position, Tip.rotation, flying: false);
            growing.Size = 0.04f;
        }

        protected override void ChargeTick(float progress, float elapsed)
        {
            if (growing == null) return;
            growing.Size = Mathf.Lerp(0.04f, 1f, progress);        // grows from a dot to full size
            growing.transform.SetPositionAndRotation(Tip.position + Tip.forward * (FireballFx.FullRadius * growing.Size * 0.9f), Tip.rotation);
        }

        protected override void ChargeEnd()
        {
            if (growing != null) Destroy(growing.gameObject);
            growing = null;
        }

        protected override void Launch()
        {
            int id = NextId();
            Vector3 start = Tip.position + Tip.forward * (FireballFx.FullRadius * 0.9f);
            Fireball ball = Fireball.Spawn(this, id, start, Tip.rotation, IsHost);
            if (ball != null) Track(id, ball.gameObject);
        }

        protected override void ImpactEffect(Vector3 position) => Fireball.Explode(position, fireSound);
    }
}
