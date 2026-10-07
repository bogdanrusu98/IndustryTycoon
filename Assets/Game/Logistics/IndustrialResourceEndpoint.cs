using System;
using IndustryTycoon.Core;

namespace IndustryTycoon.Logistics
{
    public enum IndustrialReservationKind
    {
        Outgoing = 0,
        Incoming = 1
    }

    public readonly struct IndustrialResourceReservation
    {
        internal IndustrialResourceReservation(
            IIndustrialResourceEndpoint endpoint,
            uint token,
            IndustrialReservationKind kind,
            ResourceType resourceType,
            int amount)
        {
            Endpoint = endpoint;
            Token = token;
            Kind = kind;
            ResourceType = resourceType;
            Amount = amount;
        }

        internal IIndustrialResourceEndpoint Endpoint { get; }
        internal uint Token { get; }
        public IndustrialReservationKind Kind { get; }
        public ResourceType ResourceType { get; }
        public int Amount { get; }
        public bool IsValid => Endpoint != null && Endpoint.IsReservationValid(this);
    }

    /// <summary>
    /// Narrow, data-driven contract for physical resource buffers. A route owns no
    /// inventory: it only coordinates endpoint reservations and one atomic commit.
    /// </summary>
    public interface IIndustrialResourceEndpoint
    {
        string EndpointId { get; }
        bool IsEndpointOperational { get; }
        int GetAvailable(ResourceType resourceType);
        int GetAvailableCapacity(ResourceType resourceType);
        bool TryReserveOutgoing(
            ResourceType resourceType,
            int amount,
            out IndustrialResourceReservation reservation);
        bool TryReserveIncoming(
            ResourceType resourceType,
            int amount,
            out IndustrialResourceReservation reservation);
        bool IsReservationValid(IndustrialResourceReservation reservation);
        bool ReleaseReservation(IndustrialResourceReservation reservation);

        // These two methods are deliberately mutation-only. Notifications are
        // published after both sides have committed so listeners never observe a
        // half transfer.
        void FinalizeReservationForTransfer(
            IndustrialResourceReservation reservation);
        void PublishTransferCommitted(
            IndustrialResourceReservation reservation);
    }

    public static class IndustrialResourceTransferTransaction
    {
        public static bool TryCommit(
            IIndustrialResourceEndpoint source,
            IIndustrialResourceEndpoint destination,
            ResourceType resourceType,
            int amount)
        {
            if (source == null
                || destination == null
                || ReferenceEquals(source, destination)
                || amount <= 0
                || !source.IsEndpointOperational
                || !destination.IsEndpointOperational
                || !source.TryReserveOutgoing(
                    resourceType,
                    amount,
                    out IndustrialResourceReservation outgoing))
            {
                return false;
            }

            if (!destination.TryReserveIncoming(
                    resourceType,
                    amount,
                    out IndustrialResourceReservation incoming))
            {
                source.ReleaseReservation(outgoing);
                return false;
            }

            if (!outgoing.IsValid || !incoming.IsValid)
            {
                source.ReleaseReservation(outgoing);
                destination.ReleaseReservation(incoming);
                return false;
            }

            // Reservations make both mutations infallible. No callback is emitted
            // until both endpoints have changed ownership.
            source.FinalizeReservationForTransfer(outgoing);
            destination.FinalizeReservationForTransfer(incoming);
            source.PublishTransferCommitted(outgoing);
            destination.PublishTransferCommitted(incoming);
            return true;
        }
    }
}
