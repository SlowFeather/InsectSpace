namespace InsectSpace.Gameplay.Demo
{
    public enum DemoQuestStage { MeetGuide, VisitGarden, FinishPractice, ReadyToSubmit, PendingReceipt, Complete }

    // LOCAL FIXTURE: this counter represents a demonstration receipt, never real inventory.
    public sealed class LocalQuestLesson
    {
        public DemoQuestStage Stage { get; private set; }
        public int DemonstrationReceipts { get; private set; }
        public string Story => Stage == DemoQuestStage.MeetGuide
            ? "园中向导：先认识你的本命蛊，再去药圃观察灵虫。" : "教学章节 · 药圃初行（原创本地示例）";
        public bool Advance(DemoQuestStage expected)
        {
            if (Stage != expected || expected >= DemoQuestStage.ReadyToSubmit) return false;
            Stage++; return true;
        }
        public bool Submit()
        {
            if (Stage != DemoQuestStage.ReadyToSubmit) return false;
            Stage = DemoQuestStage.PendingReceipt;
            return true;
        }
        public bool ApplyFixtureReceipt()
        {
            if (Stage != DemoQuestStage.PendingReceipt) return false;
            DemonstrationReceipts++;
            Stage = DemoQuestStage.Complete;
            return true;
        }
        public void Reset() { Stage = DemoQuestStage.MeetGuide; DemonstrationReceipts = 0; }
    }
}
