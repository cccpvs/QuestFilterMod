using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace QuestFilterMod.RandomQuests
{
    public partial class RandomQuestGenerator

    {
        private Quest? GenerateBaseQuest(string type, Action<Quest, Func<MongoId>> build)
        {
            var idFactory = new Func<MongoId>(() => new MongoId(Guid.NewGuid().ToString("N")[..24]));
            var questId = idFactory();

            var quest = new Quest
            {
                Id = questId,
                Name = $"{questId} name",
                QuestName = $"{questId} questName",
                Description = $"{questId} description",
                Note = $"{questId} note",
                TraderId = new MongoId(_config.TraderIds.RandomItem(_random)),
                Side = "Pmc",
                Location = "any",
                Image = _config.DefaultQuest.Image ?? "/files/quest/icon/default.jpg",
                Type = QuestTypeEnum.PickUp,
                CanShowNotificationsInGame = true,
                Restartable = false,
                RankingModes = [],
                SecretQuest = false,
                Status = 0,
                
                Conditions = new QuestConditionTypes
                {
                    AvailableForStart = new(),
                    AvailableForFinish = new(),
                    Fail = new()
                },
                Rewards = new Dictionary<string, List<Reward>>
                {
                    ["Started"] = new(),
                    ["Success"] = new(),
                    ["Fail"] = new()
                }
            };

            // Устанавливаем не-required поля
            quest.InstantComplete = false;
            quest.IsKey = false;
            quest.ProgressSource = "eft";
            quest.AcceptanceAndFinishingSource = "eft";
            quest.AcceptPlayerMessage = $"{questId} accept";
            quest.ChangeQuestMessageText = $"{questId} change";
            quest.CompletePlayerMessage = $"{questId} complete";
            quest.StartedMessageText = $"{questId} started";
            quest.SuccessMessageText = $"{questId} completed";
            quest.FailMessageText = $"{questId} failed";
            quest.GameModes = new();
            quest.RankingModes = new();

            build(quest, idFactory);

            if (string.IsNullOrEmpty(quest.Location))
            {
                if (Plugin._config.Debug)
                    _logger.Info($"[QuestFilterMod][QuestGenerationBase] ❌ Quest {quest.Id} doesn't have Location");
                return null;
            }


            AddRewards(quest);
            CreateAndRegisterQuest(quest);
            if (Plugin._config.Debug)
                _logger.Info($"[QuestFilterMod][QuestGenerationBase] ✅ Quest '{quest.Id}' ({type}) created");

#if DEBUG
            // 🔹 Логируем квест в консоль как JSON
            var json = System.Text.Json.JsonSerializer.Serialize(quest, new System.Text.Json.JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true  // для читаемости
            });

            //_logger.Info($"[QuestFilterMod][QuestGenerationBase] 📜 Quest '{quest.Id}' ({type}) generated:\n{json}");
#endif

            return quest;
        }
        private void AddRewards(Quest quest)
        {
            AddExperienceReward(quest);
            AddMoneyReward(quest);
            AddRandomItemRewards(quest);
            AddTraderStandingReward(quest);
        }
    }
}
