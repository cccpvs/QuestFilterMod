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
     * 
     * 
     * 
     * [Запрос клиента] 26.196.29.49 /client/repeatalbeQuests/activityPeriods
Не найден статус квеста для: Elimination
Не удалось сгенерировать квест elimination — отсутствует шаблон квеста
Error handling request: /client/repeatalbeQuests/activityPeriods
Object reference not set to an instance of an object.
   at SPTarkov.Server.Core.Controllers.RepeatableQuestController.GetClientRepeatableQuests(MongoId sessionID)
   at SPTarkov.Server.Core.Callbacks.QuestCallbacks.ActivityPeriods(String url, EmptyRequestData _, MongoId sessionID)
   at SPTarkov.Server.Core.Routers.Static.QuestStaticRouter.<>c__DisplayClass0_0.<<-ctor>b__1>d.MoveNext()
--- End of stack trace from previous location ---
   at SPTarkov.Server.Core.DI.RouteAction`1.<>c__DisplayClass0_0.<<-ctor>b__0>d.MoveNext()
--- End of stack trace from previous location ---
   at SPTarkov.Server.Core.DI.StaticRouter.HandleStatic(String url, String body, MongoId sessionId, String output)
   at SPTarkov.Server.Core.Routers.HttpRouter.HandleRoute(HttpRequest request, MongoId sessionID, ResponseWrapper wrapper, IEnumerable`1 routers, Boolean dynamic, String body)
   at SPTarkov.Server.Core.Routers.HttpRouter.GetResponse(HttpRequest req, MongoId sessionID, String body)
   at FikaServer.Overrides.Routers.GetResponseOverride.Postfix(ValueTask`1 __result, HttpRequest req)
   at CompoundingPerf.Features.CachingHttpRouter.GetResponse(HttpRequest req, MongoId sessionID, String body)
   at SPTarkov.Server.Core.Servers.Http.SptHttpListener.GetResponse(MongoId sessionId, HttpContext context, String body)
   at SPTarkov.Server.Core.Servers.Http.SptHttpListener.Handle(MongoId sessionId, HttpContext context)
   at SPTarkov.Server.Core.Servers.HttpServer.HandleRequest(HttpContext context, RequestDelegate next)
   at SPTarkov.Server.Program.<>c.<<ConfigureWebApp>b__3_0>d.MoveNext()
--- End of stack trace from previous location ---
   at SPTarkov.Server.Services.NoGCRegionMiddleware.InvokeAsync(HttpContext context)
   at SPTarkov.Server.Logger.SptLoggerMiddleware.InvokeAsync(HttpContext context)
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
    }
}