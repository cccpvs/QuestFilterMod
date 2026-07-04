//Clear.cs

using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Common;

namespace QuestFilterMod.RepeatableQuestCleaner
{

#if DEBUG
    /*
    */
#endif

    public class Clear
    {
        private readonly ISptLogger<Plugin> _logger;
        private readonly DatabaseService _databaseService;
        private RepeatableQuestDatabase _questDatabase;

        public Clear(
            ISptLogger<Plugin> logger,
            DatabaseService databaseService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        }
        public void SetQuestDatabase(RepeatableQuestDatabase database)
        {
            _questDatabase = database ?? throw new ArgumentNullException(nameof(database));
#if DEBUG
            EnsureInitializedDatabase();

            ClearAllTemplates();
#endif
        }
#if DEBUG
        public RepeatableQuestDatabase EnsureInitializedDatabase()
        {
            _questDatabase ??= new RepeatableQuestDatabase
            {
                Samples = new List<SampleQuests>(),
                Templates = new RepeatableTemplates()
            };

            _questDatabase.Templates ??= new RepeatableTemplates();
            _questDatabase.Templates.Elimination ??= CreateQuestTemplate(QuestTypeEnum.Elimination);
            _questDatabase.Templates.Completion ??= CreateQuestTemplate(QuestTypeEnum.Completion);
            _questDatabase.Templates.Exploration ??= CreateQuestTemplate(QuestTypeEnum.Exploration);
            _questDatabase.Templates.Pickup ??= CreateQuestTemplate(QuestTypeEnum.PickUp);


            return _questDatabase;
        }
#endif
#if DEBUG
        public RepeatableQuestDatabase GetQuestDatabase() => _questDatabase;
        public void ClearAllQuests()
        {
            var db = EnsureInitializedDatabase(); 

            var count = db.Samples?.Count ?? 0;
            db.Samples?.Clear();

            if (count > 0 && Plugin.Config.Debug)
                _logger.Info($"[QuestFilterMod][ClearRepetableQuest] Removed {count} quests from Samples.");
        }
#endif
#if DEBUG
        public void ClearAllTemplates()
        {
            _questDatabase ??= new RepeatableQuestDatabase();
            _questDatabase.Templates ??= new RepeatableTemplates();

            _questDatabase.Templates.Elimination = CreateQuestTemplate(QuestTypeEnum.Elimination);
            _questDatabase.Templates.Completion = CreateQuestTemplate(QuestTypeEnum.Completion);
            _questDatabase.Templates.Exploration = CreateQuestTemplate(QuestTypeEnum.Exploration);
            _questDatabase.Templates.Pickup = CreateQuestTemplate(QuestTypeEnum.PickUp);

            _questDatabase.Templates.ExtensionData?.Clear();

            if (Plugin.Config.Debug)
                _logger.Info("[QuestFilterMod][ClearRepetableQuest] Replaced all repeatable quest templates.");
        }
#endif
#if DEBUG
        private static RepeatableQuest CreateQuestTemplate(QuestTypeEnum type)
        {
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
                Status = 0,
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
#endif
    }
}