using SPTarkov.Server.Core.Models.Eft.Common.Tables;
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

            _questDatabase = new RepeatableQuestDatabase();
        }

        public void SetQuestDatabase(RepeatableQuestDatabase database)
        {
            _questDatabase = database ?? throw new ArgumentNullException(nameof(database));
        }

        public RepeatableQuestDatabase GetQuestDatabase() => _questDatabase;

        /// <summary>
        /// Очищает все шаблоны квестов в Samples
        /// </summary>
        public void ClearAllQuests()
        {
            var count = _questDatabase.Samples?.Count ?? 0;
            _questDatabase.Samples?.Clear();

            if (count > 0 && Plugin._config.Debug)
                _logger.Info($"[QuestFilterMod][ClearRepetableQuest] Removed {count} quests from Samples.");
        }

        /// <summary>
        /// Полностью очищает шаблоны квестов в Templates (Elimination, Completion, и т.д.)
        /// Включая ExtensionData.
        /// </summary>
        public void ClearAllTemplates()
        {
            if (_questDatabase.Templates == null)
            {
                if (Plugin._config.Debug)
                    _logger.Info("[QuestFilterMod][ClearRepetableQuest] No Templates found — nothing to clear.");
                return;
            }

            int totalRemoved = 0;

            // ✔ Удаляем стандартные типы
            totalRemoved += RemoveAndCount(_questDatabase.Templates.Elimination, "Elimination");
            _questDatabase.Templates.Elimination = null;

            totalRemoved += RemoveAndCount(_questDatabase.Templates.Completion, "Completion");
            _questDatabase.Templates.Completion = null;

            totalRemoved += RemoveAndCount(_questDatabase.Templates.Exploration, "Exploration");
            _questDatabase.Templates.Exploration = null;

            totalRemoved += RemoveAndCount(_questDatabase.Templates.Pickup, "Pickup");
            _questDatabase.Templates.Pickup = null;

            // ✔ Удаляем ExtensionData (кастомные шаблоны, если есть)
            if (_questDatabase.Templates.ExtensionData != null)
            {
                totalRemoved += _questDatabase.Templates.ExtensionData.Count;
                _questDatabase.Templates.ExtensionData.Clear();
            }

            if (totalRemoved > 0 && Plugin._config.Debug)
                _logger.Info($"[QuestFilterMod][ClearRepetableQuest] Removed {totalRemoved} quest templates from Templates.");
        }

        private static int RemoveAndCount(RepeatableQuest? rq, string typeName)
        {
            return rq != null ? 1 : 0; // удалили один шаблон (или 0, если null)
        }
    }
}