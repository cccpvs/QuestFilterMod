using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;

namespace QuestFilterMod.RepeatableQuestCleaner
{
#if DEBUG
    /*
     * 1. Полноценно не понятно как сработает удаление временных квестов. Нужна проверка.
    */
#endif
    public class ClearRepetableQuest
    {
        private readonly ISptLogger<Plugin> _logger;
        private readonly DatabaseService _databaseService;

        private RepeatableQuestDatabase _questDatabase;

        public ClearRepetableQuest(
            ISptLogger<Plugin> logger,
            DatabaseService databaseService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        }

        public void SetQuestDatabase(RepeatableQuestDatabase database)
        {
            _questDatabase = database ?? throw new ArgumentNullException(nameof(database));
        }

        public RepeatableQuestDatabase GetQuestDatabase() => _questDatabase;

        public void ClearAllQuests()
        {
            var count = _questDatabase.Samples?.Count ?? 0;
            _questDatabase.Samples?.Clear();

            if (count > 0 && Plugin.Config.Debug)
                _logger.Info($"[QuestFilterMod][ClearRepetableQuest] Removed {count} quests from Samples.");
        }

        public void ClearAllTemplates()
        {
            // 🔁 Гарантированная инициализация Templates
            _questDatabase.Templates ??= new RepeatableTemplates();

            // ✅ Пересоздаём каждый шаблон с валидными данными (в т.ч. с правильным статусом)
            _questDatabase.Templates.Elimination = CreateQuestTemplate(QuestTypeEnum.Elimination);
            _questDatabase.Templates.Completion = CreateQuestTemplate(QuestTypeEnum.Completion);
            _questDatabase.Templates.Exploration = CreateQuestTemplate(QuestTypeEnum.Exploration);
            _questDatabase.Templates.Pickup = CreateQuestTemplate(QuestTypeEnum.PickUp);

            // 🧹 Очищаем ExtensionData, если есть
            _questDatabase.Templates.ExtensionData?.Clear();

            if (Plugin.Config.Debug)
                _logger.Info("[QuestFilterMod][ClearRepetableQuest] Replaced all repeatable quest templates.");
        }

        private static RepeatableQuest CreateQuestTemplate(QuestTypeEnum type)
        {
            // ✅ Обязательно: Status — это строка (не enum!)
            // В SPT — это один из: "Available", "Active", "Completed", "Failed"
            // Делай его всегда "Available" для временных/новых шаблонов.

            return new RepeatableQuest
            {
                Id = new MongoId(),
                Type = type,
                Name = $"Placeholder {type}",
                Description = "Placeholder",
                Location = "factory4_day",
                Image = "placeholder_icon.png",
                Side = "Usec",
                TraderId = "579dc57fd2720b3c368b45ee",
                Status = 0, // ← Ключевое поле!
                Conditions = new QuestConditionTypes
                {
                    Started = new List<QuestCondition>(),
                    AvailableForFinish = new List<QuestCondition>(),
                    AvailableForStart = new List<QuestCondition>(),
                    Success = new List<QuestCondition>(),
                    Fail = new List<QuestCondition>()
                },
                ChangeCost = new List<ChangeCost>(),
                CanShowNotificationsInGame = false,
                Restartable = false,
                ChangeStandingCost = 0,
                SptRepatableGroupName = null
            };
        }
    }
}