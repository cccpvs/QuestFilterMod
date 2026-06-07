using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace QuestFilterMod.RepeatableQuest
{

#if DEBUG
    public class SimpleRepeatableQuestGenerator
    {

        private readonly Random _random = new Random();
        private readonly Dictionary<string, SampleQuests> _templateMap;

        public SimpleRepeatableQuestGenerator(Dictionary<string, SampleQuests> templateMap)
        {
            _templateMap = templateMap ?? throw new ArgumentNullException(nameof(templateMap));
        }

        // Генерируем N временных квестов
        public List<SampleQuests> Generate(int count = 5)
        {
            var templates = _templateMap.Values.Where(q => q != null).ToList();
            if (!templates.Any()) return new List<SampleQuests>();

            // Перемешиваем и выбираем случайные шаблоны
            var selected = templates.OrderBy(_ => _random.Next()).Take(count);

            return selected.Select(CreateTemporaryQuest).ToList();
        }

        private SampleQuests CreateTemporaryQuest(SampleQuests template)
        {
            return new SampleQuests
            {
                Id = Guid.NewGuid().ToString(), // новый ID
                TraderId = template.TraderId,
                Location = template.Location,
                Image = template.Image,
                Type = "Temporary", // важное поле
                IsKey = template.IsKey ?? false,
                Restartable = true,
                InstantComplete = false,
                SecretQuest = template.SecretQuest ?? false,
                CanShowNotificationsInGame = template.CanShowNotificationsInGame ?? true,
                Conditions = DeepCopyConditions(template.Conditions),
                Rewards = GetDefaultRewards() ?? new Dictionary<string, List<Reward>>(),
                Name = ReplacePlaceholders(template.Name, template.Id, template.TraderId),
                Note = ReplacePlaceholders(template.Note, template.Id, template.TraderId),
                Description = ReplacePlaceholders(template.Description, template.Id, template.TraderId),
                SuccessMessageText = ReplacePlaceholders(template.SuccessMessageText, template.Id, template.TraderId),
                FailMessageText = ReplacePlaceholders(template.FailMessageText, template.Id, template.TraderId),
                StartedMessageText = ReplacePlaceholders(template.StartedMessageText, template.Id, template.TraderId),

                TemplateId = template.Id,
                

            };
        }

        private static QuestConditionTypes DeepCopyConditions(QuestConditionTypes source)
        {
            if (source == null) return new QuestConditionTypes();

            return new QuestConditionTypes
            {
                AvailableForStart = CopyList(source.AvailableForStart),
                AvailableForFinish = CopyList(source.AvailableForFinish),
                Fail = CopyList(source.Fail)
                
            };
        }

        private static List<T> CopyList<T>(List<T> source)
        {
            return source?.Select(x => x).ToList() ?? new List<T>();
        }

        private static string ReplacePlaceholders(string? text, string? templateId, string? traderId)
        {
            return text?
                .Replace("{templateId}", templateId ?? "")
                .Replace("{traderId}", traderId ?? "")
               ?? "";
        }

        private static int GetValueOrDefault(int? value, int defaultValue)
        {
            return value ?? defaultValue;
        }

        private Dictionary<string, List<Reward>> GetDefaultRewards()
        {
            return new Dictionary<string, List<Reward>>
            {
                ["Success"] = new List<Reward>
                {
                    new Reward
                    {
                        Target = "5449016a4bdc2d6f028b456f",
                        Value = 50000,
                    }
                },
                ["Started"] = new List<Reward>(),
                ["Fail"] = new List<Reward>()
            };
        }
    }
#endif
}