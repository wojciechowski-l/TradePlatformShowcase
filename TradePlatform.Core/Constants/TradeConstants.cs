namespace TradePlatform.Core.Constants
{
    public static class TradeClaimTypes
    {
        public const string AccountId = "urn:tradeplatform:accountid";
    }

    public static class TradeDefaults
    {
        public const decimal InitialAccountBalance = 1000m;
    }

    public static class MessagingConstants
    {
        public const string OrdersQueue = "trade-orders";
        public const string OrdersDeadLetterQueue = "trade-orders.dead";

        public const string NotificationsQueue = "trade-notifications";
        public const string NotificationsExchange = "trade-notifications-x";
        public const string NotificationsDeadLetterQueue = "trade-notifications.dead";

        public const string RetryHeader = "x-retry-count";
    }

    public enum TransactionStatus
    {
        Pending,
        Validated,
        Processing,
        Processed,
        Failed
    }

    public enum AccountActivityDirection
    {
        Outgoing,
        Incoming
    }
}
