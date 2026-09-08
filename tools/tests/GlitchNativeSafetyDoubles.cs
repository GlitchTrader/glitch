// Native boundary doubles. They deliberately expose Initialized orders immediately
// on CreateOrder, as NinjaTrader did in the 2026-09-08 incident. No live NT assembly,
// account, data directory, connection, or order endpoint is used by this harness.
using System;
using System.Collections.Generic;
using System.Linq;

namespace NinjaTrader.Cbi
{
    public enum OrderState { Initialized, Submitted, Accepted, Working, PartFilled, Filled,
        CancelPending, Cancelled, Rejected, TriggerPending, AcceptedByRisk, ChangeSubmitted,
        ChangePending, Suspended }
    public enum OrderAction { Buy, BuyToCover, Sell, SellShort }
    public enum OrderType { Market, Limit, StopMarket, StopLimit }
    public enum OrderEntry { Automated }
    public enum TimeInForce { Day, Gtc }
    public enum MarketPosition { Flat, Long, Short }
    public enum Operation { Add, Update, Remove }
    public enum ErrorCode { NoError }
    public class Quote { public double Price { get; set; } }
    public class MarketData { public Quote Bid = new Quote(); public Quote Ask = new Quote(); }
    public class MasterInstrument
    {
        public double TickSize = 0.1;
        public double PointValue = 5;
        public double RoundToTickSize(double value)
            => Math.Round(value / TickSize, MidpointRounding.AwayFromZero) * TickSize;
    }
    public class Instrument
    {
        public static readonly Dictionary<string, Instrument> Registry = new Dictionary<string, Instrument>();
        public string FullName;
        public MasterInstrument MasterInstrument = new MasterInstrument();
        public MarketData MarketData = new MarketData();
        public static Instrument GetInstrument(string name, bool _) => Registry[name];
    }
    public class Order
    {
        public Account Account;
        public Instrument Instrument;
        public string Name, Oco, OrderId;
        public int Id, Quantity, Filled;
        public double StopPrice, LimitPrice, StopPriceChanged, LimitPriceChanged, AverageFillPrice;
        public OrderState OrderState;
        public OrderAction OrderAction;
        public OrderType OrderType;
    }
    public class Position
    {
        public Account Account;
        public Instrument Instrument;
        public MarketPosition MarketPosition;
        public int Quantity;
        public double AveragePrice;
    }
    public class Execution
    {
        public Account Account;
        public Instrument Instrument;
        public Order Order;
        public string ExecutionId;
        public int Quantity;
        public double Price, Commission;
    }
    public class AccountStatusEventArgs : EventArgs
    {
        public Account Account;
        public object PreviousStatus, Status;
    }
    public class OrderEventArgs : EventArgs
    {
        public Order Order;
        public ErrorCode Error;
        public string Comment;
    }
    public class PositionEventArgs : EventArgs
    {
        public Position Position;
        public int Quantity;
        public MarketPosition MarketPosition;
    }
    public class ExecutionEventArgs : EventArgs
    {
        public Execution Execution;
        public Operation Operation;
        public string ExecutionId;
        public int Quantity;
        public double Price;
    }
    public class Account
    {
        public static readonly List<Account> All = new List<Account>();
        public static event EventHandler<AccountStatusEventArgs> AccountStatusUpdate;
        public event EventHandler<OrderEventArgs> OrderUpdate;
        public event EventHandler<ExecutionEventArgs> ExecutionUpdate;
        public event EventHandler<PositionEventArgs> PositionUpdate;
        public string Name;
        public readonly List<Order> Orders = new List<Order>();
        public readonly List<Execution> Executions = new List<Execution>();
        public readonly List<Position> Positions = new List<Position>();
        public int Creates, Submits, Flattens, Changes;
        public Instrument[] LastFlatten;
        public Action<Account, Instrument[]> OnFlatten;
        public Order CreateOrder(Instrument instrument, OrderAction action, OrderType type,
            OrderEntry entry, TimeInForce tif, int quantity, double limit, double stop,
            string oco, string name, DateTime expiration, object callback)
        {
            var order = new Order { Account = this, Instrument = instrument, OrderAction = action,
                OrderType = type, Quantity = quantity, LimitPrice = limit, StopPrice = stop,
                Oco = oco, Name = name, Id = ++Creates, OrderId = Guid.NewGuid().ToString("N"),
                OrderState = OrderState.Initialized };
            Orders.Add(order);
            EmitOrder(order);
            return order;
        }
        public void Submit(IEnumerable<Order> orders)
        {
            Submits++;
            foreach (Order order in orders) { order.OrderState = OrderState.Working; EmitOrder(order); }
        }
        public void Change(IEnumerable<Order> orders)
        {
            Changes++;
            foreach (Order order in orders)
            {
                if (order.StopPriceChanged != 0) order.StopPrice = order.StopPriceChanged;
                if (order.LimitPriceChanged != 0) order.LimitPrice = order.LimitPriceChanged;
                EmitOrder(order);
            }
        }
        public void Cancel(IEnumerable<Order> orders)
        {
            foreach (Order order in orders)
            {
                // This distinction is the failure our old compile-only checks missed.
                order.OrderState = order.OrderState == OrderState.Initialized
                    ? OrderState.CancelPending : OrderState.Cancelled;
                EmitOrder(order);
            }
        }
        public void Flatten(IEnumerable<Instrument> instruments)
        {
            Flattens++;
            LastFlatten = instruments.ToArray();
            OnFlatten?.Invoke(this, LastFlatten);
        }
        public void EmitOrder(Order order)
            => OrderUpdate?.Invoke(this, new OrderEventArgs { Order = order, Error = ErrorCode.NoError, Comment = "" });
        public void SetPosition(Instrument instrument, int signed)
        {
            Positions.RemoveAll(p => p.Instrument == instrument);
            var position = new Position { Account = this, Instrument = instrument,
                Quantity = Math.Abs(signed), AveragePrice = 2972.1,
                MarketPosition = signed > 0 ? MarketPosition.Long : signed < 0 ? MarketPosition.Short : MarketPosition.Flat };
            if (signed != 0) Positions.Add(position);
            PositionUpdate?.Invoke(this, new PositionEventArgs { Position = position,
                Quantity = position.Quantity, MarketPosition = position.MarketPosition });
        }
        public void CompleteFlat(Instrument instrument)
        {
            foreach (Order order in Orders.Where(o => o.Instrument == instrument))
            { order.OrderState = OrderState.Cancelled; EmitOrder(order); }
            SetPosition(instrument, 0);
            var close = new Order { Account = this, Instrument = instrument,
                OrderAction = OrderAction.BuyToCover, OrderType = OrderType.Market,
                Quantity = 1, Filled = 1, OrderState = OrderState.Filled, OrderId = Guid.NewGuid().ToString("N") };
            var fill = new Execution { Account = this, Instrument = instrument, Order = close,
                ExecutionId = Guid.NewGuid().ToString("N"), Quantity = 1, Price = 2972 };
            Executions.Add(fill);
            ExecutionUpdate?.Invoke(this, new ExecutionEventArgs { Execution = fill,
                ExecutionId = fill.ExecutionId, Quantity = 1, Price = fill.Price, Operation = Operation.Add });
        }
    }
}

namespace Glitch.Infrastructure
{
    internal static class GlitchExecutionEvidenceWriter
    {
        public static readonly List<string> Codes = new List<string>();
        public static void TryAppend(string intent, string status, string code, string message, DateTime utc)
        { lock (Codes) Codes.Add(status + ":" + code); }
        public static void TryRequestEntryRangeReassessment(string intent, string action, string instrument,
            decimal low, decimal high, decimal price, DateTime utc) { }
    }
}
