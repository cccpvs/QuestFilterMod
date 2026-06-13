using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;

namespace QuestFilterMod.RepeatableQuestCleaner
{
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
            if (_questDatabase.Templates == null)
            {
                if (Plugin.Config.Debug)
                    _logger.Info("[QuestFilterMod][ClearRepetableQuest] No Templates found — nothing to clear.");
                return;
            }

            int totalRemoved = 0;

            totalRemoved += RemoveAndCount(_questDatabase.Templates.Elimination, "Elimination");
            _questDatabase.Templates.Elimination = CreateQuestTemplate(QuestTypeEnum.Elimination);

            totalRemoved += RemoveAndCount(_questDatabase.Templates.Completion, "Completion");
            _questDatabase.Templates.Completion = CreateQuestTemplate(QuestTypeEnum.Completion);

            totalRemoved += RemoveAndCount(_questDatabase.Templates.Exploration, "Exploration");
            _questDatabase.Templates.Exploration = CreateQuestTemplate(QuestTypeEnum.Exploration);

            totalRemoved += RemoveAndCount(_questDatabase.Templates.Pickup, "Pickup");
            _questDatabase.Templates.Pickup = CreateQuestTemplate(QuestTypeEnum.PickUp);

            if (_questDatabase.Templates.ExtensionData != null)
            {
                totalRemoved += _questDatabase.Templates.ExtensionData.Count;
                _questDatabase.Templates.ExtensionData.Clear();
            }

            if (totalRemoved > 0 && Plugin.Config.Debug)
                _logger.Info($"[QuestFilterMod][ClearRepetableQuest] Removed {totalRemoved} quest templates from Templates.");
        }
        private static RepeatableQuest CreateQuestTemplate(QuestTypeEnum type)
        {
            var questId = new MongoId();

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
                SptRepatableGroupName = null,
                QuestStatus = null,
            };
        }

        private static int RemoveAndCount(RepeatableQuest? rq, string typeName)
        {
            return rq is not null ? 1 : 0;
        }
    }
}