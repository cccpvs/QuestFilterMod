using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using System.Text.Json;

namespace QuestFilterMod.RepeatableQuest
{
    public class ClearRepetableQuest
    {
        private readonly ISptLogger<Plugin> _logger; // ← Изменили тип: теперь совпадает с Plugin
        private readonly DatabaseService _databaseService;

        private RepeatableQuestDatabase _questDatabase;

        public ClearRepetableQuest(
            ISptLogger<Plugin> logger,
            DatabaseService databaseService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));

            //_questDatabase = new RepeatableQuestDatabase();
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
            if (_questDatabase?.Samples == null) return;

            var count = _questDatabase.Samples.Count(q => q?.Type == "Temporary");
            _questDatabase.Samples.RemoveAll(q => q?.Type == "Temporary");

            if (count > 0)
                if (Plugin._config.Debug)
                    _logger.Info($"[QuestFilterMod][TemporaryQuest] Удалено {count} временных квестов.");
        }

        /// <summary>
        /// Полностью очищает все шаблоны квестов в Templates (Elimination, Completion и др.)
        /// С отладочной информацией: сколько было, сколько очищено.
        /// </summary>
        public void ClearAllTemplates()
        {
            if (Plugin._config.Debug)
                _logger.Info("[QuestFilterMod][TemporaryQuest] ✅ Вход в ClearAllTemplates() — начало очистки.");

            if (_questDatabase?.Templates == null)
            {
                if (Plugin._config.Debug)
                    _logger.Info("[QuestFilterMod][TemporaryQuest] ❌ Templates отсутствуют — пропускаем очистку.");
                return;
            }

            if (_questDatabase?.Templates == null)
            {
                if (Plugin._config.Debug)
                    _logger.Info("[QuestFilterMod][TemporaryQuest] Templates отсутствуют — пропускаем очистку.");
                return;
            }

            var templates = _questDatabase.Templates;
            int initialCount = 0;
            int clearedCount = 0;

            // Подсчитываем, сколько шаблонов изначально было задано
            if (templates.Elimination != null) initialCount++;
            if (templates.Completion != null) initialCount++;
            if (templates.Exploration != null) initialCount++;
            if (templates.Pickup != null) initialCount++;

            if (Plugin._config.Debug)
                _logger.Info($"[QuestFilterMod][TemporaryQuest] Найдено {initialCount} шаблонов квестов до очистки.");

            // Очищаем и считаем
            if (templates.Elimination != null) { templates.Elimination = null; clearedCount++; }
            if (templates.Completion != null) { templates.Completion = null; clearedCount++; }
            if (templates.Exploration != null) { templates.Exploration = null; clearedCount++; }
            if (templates.Pickup != null) { templates.Pickup = null; clearedCount++; }

            if (Plugin._config.Debug)
            {
                if (clearedCount > 0)
                    _logger.Info($"[QuestFilterMod][TemporaryQuest] Успешно очищено {clearedCount} шаблонов квестов.");
                else
                    _logger.Info("[QuestFilterMod][TemporaryQuest] Нечего очищать — все шаблоны уже пусты.");
            }
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
                _logger.Info($"[QuestFilterMod][TemporaryQuest] Удалено {count} квестов по условию.");
        }
    }
}