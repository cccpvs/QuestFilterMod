using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;

namespace QuestFilterMod.RepeatableQuest
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
            //_questDatabase = null;
            
        }

        /// <summary>
        /// Устанавливает базу квестов (если нужно подменить извне)
        /// </summary>
        public void SetQuestDatabase(RepeatableQuestDatabase database)
        {
            _questDatabase = database ?? throw new ArgumentNullException(nameof(database));
        }

        /// <summary>
        /// Возвращает текущую базу
        /// </summary>
        public RepeatableQuestDatabase GetQuestDatabase() => _questDatabase;

        /// <summary>
        /// Удаляет все временные квесты (по типу "Temporary")
        /// </summary>
        public void ClearTemporaryQuests()
        {
            var count = _questDatabase.Samples.RemoveAll(q => q?.Type == "Temporary");
            if (count > 0 && Plugin._config.Debug)
                _logger.Info($"[QuestFilterMod][ClearRepetableQuest] Removed {count} temporary quests.");
        }

        /// <summary>
        /// Полностью очищает все шаблоны квестов в Templates (Elimination, Completion и др.)
        /// С отладочной информацией: сколько было, сколько очищено.
        /// </summary>
        public void ClearAllQuests()
        {
            var count = _questDatabase.Samples?.Count ?? 0;
            _questDatabase.Samples?.Clear();

            if (count > 0 && Plugin._config.Debug)
                _logger.Info($"[QuestFilterMod][ClearRepetableQuest] Removed {count} total quests.");
        }

        /// <summary>
        /// Очищает квесты по кастомному условию
        /// </summary>
        public void ClearIf(Func<SampleQuests, bool> predicate)
        {
            if (_questDatabase?.Samples == null) return;
            var count = _questDatabase.Samples.Count(q => q != null && predicate(q));
            _questDatabase.Samples.RemoveAll(q => q != null && predicate(q));

            if (count > 0)
                _logger.Info($"[QuestFilterMod][ClearRepetableQuest] Removed {count} conditional quests.");
        }
    }
}