using InsectSpace.Contracts;

namespace InsectSpace.Gameplay.Demo
{
    // Explicit local read-model fixture. Nothing here submits persistence or awards currency.
    public sealed class LocalPlayerLesson
    {
        private PlayerSummary player;
        public PlayerSummary Player => player == null ? null : Copy(player);
        public int PreviewEquipment { get; private set; }
        public int PreviewGu { get; private set; }

        public bool Apply(PlayerSummary snapshot)
        {
            if (snapshot == null || snapshot.PlayerId <= 0 || snapshot.Revision < 1) return false;
            if (player != null && (snapshot.PlayerId != player.PlayerId || snapshot.Revision <= player.Revision)) return false;
            player = Copy(snapshot);
            return true;
        }

        public void PreviewLoadout(int equipment, int gu)
        { PreviewEquipment = equipment; PreviewGu = gu; }

        public void Reset() { player = null; PreviewEquipment = 0; PreviewGu = 0; }

        public static PlayerSummary Fixture(long revision = 1) => new PlayerSummary
        {
            PlayerId = 1001, DisplayName = "本地学员", HomeRealmId = "demo-home",
            CharacterConfigId = 1, RealmLevel = 1, Revision = revision
        };

        private static PlayerSummary Copy(PlayerSummary value) => new PlayerSummary
        {
            PlayerId = value.PlayerId, DisplayName = value.DisplayName, HomeRealmId = value.HomeRealmId,
            CharacterConfigId = value.CharacterConfigId, RealmLevel = value.RealmLevel, Revision = value.Revision
        };
    }
}
