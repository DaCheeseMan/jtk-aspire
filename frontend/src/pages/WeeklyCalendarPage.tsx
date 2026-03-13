import { useEffect, useState, useCallback } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useAuth } from 'react-oidc-context';
import { bookingsApi, courtsApi, setAuthToken, type Court, type CourtBooking } from '../api/client';
import './WeeklyCalendarPage.css';

const HOURS = Array.from({ length: 16 }, (_, i) => i + 7); // 07–22
const DAY_NAMES = ['Mån', 'Tis', 'Ons', 'Tor', 'Fre', 'Lör', 'Sön'];

function getMondayOf(date: Date): Date {
  const d = new Date(date);
  const day = d.getDay(); // 0=Sun
  const diff = day === 0 ? -6 : 1 - day;
  d.setDate(d.getDate() + diff);
  d.setHours(0, 0, 0, 0);
  return d;
}

function addDays(date: Date, days: number): Date {
  const d = new Date(date);
  d.setDate(d.getDate() + days);
  return d;
}

function toDateStr(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

function formatDate(date: Date): string {
  return date.toLocaleDateString('sv-SE', { day: 'numeric', month: 'short' });
}

type SlotState = 'past' | 'free' | 'mine' | 'taken';

interface SlotInfo {
  state: SlotState;
  booking?: CourtBooking;
}

export function WeeklyCalendarPage() {
  const { courtId } = useParams<{ courtId: string }>();
  const navigate = useNavigate();
  const auth = useAuth();

  const [court, setCourt] = useState<Court | null>(null);
  const [weekStart, setWeekStart] = useState<Date>(() => getMondayOf(new Date()));
  const [courtBookings, setCourtBookings] = useState<CourtBooking[]>([]);
  const [myFutureCount, setMyFutureCount] = useState(0);
  const [loadingSlot, setLoadingSlot] = useState<string | null>(null); // "YYYY-MM-DD-HH"
  const [error, setError] = useState<string | null>(null);
  const [confirmedSlot, setConfirmedSlot] = useState<string | null>(null); // "YYYY-MM-DD-HH"

  const myUserId = auth.user?.profile.sub;

  const weekDays = Array.from({ length: 7 }, (_, i) => addDays(weekStart, i));
  const weekFrom = toDateStr(weekDays[0]);
  const weekTo = toDateStr(weekDays[6]);

  const loadBookings = useCallback(async () => {
    if (!courtId) return;
    try {
      const [weekData, myData] = await Promise.all([
        bookingsApi.getForCourt(Number(courtId), weekFrom, weekTo),
        bookingsApi.getMine(),
      ]);
      setCourtBookings(weekData);
      const now = new Date();
      const todayStr = toDateStr(now);
      const nowHour = now.getHours();
      const future = myData.filter(b =>
        b.date > todayStr || (b.date === todayStr && parseInt(b.startTime.slice(0, 2)) > nowHour)
      );
      setMyFutureCount(future.length);
    } catch {
      setError('Kunde inte ladda bokningar.');
    }
  }, [courtId, weekFrom, weekTo]);

  useEffect(() => {
    if (auth.user?.access_token) setAuthToken(auth.user.access_token);
    if (courtId) {
      courtsApi.getById(Number(courtId)).then(setCourt).catch(() => setError('Bana hittades inte.'));
    }
  }, [auth.user, courtId]);

  useEffect(() => {
    loadBookings();
  }, [loadBookings]);

  function getSlotInfo(dateStr: string, hour: number): SlotInfo {
    const now = new Date();
    const todayStr = toDateStr(now);
    const nowHour = now.getHours();

    const isPast = dateStr < todayStr || (dateStr === todayStr && hour <= nowHour);
    if (isPast) return { state: 'past' };

    const booking = courtBookings.find(b => {
      const bHour = parseInt(b.startTime.slice(0, 2));
      return b.date === dateStr && bHour === hour;
    });

    if (!booking) return { state: 'free' };
    if (booking.userId === myUserId) return { state: 'mine', booking };
    return { state: 'taken', booking };
  }

  async function handleSlotClick(dateStr: string, hour: number) {
    const slotKey = `${dateStr}-${hour}`;
    const { state } = getSlotInfo(dateStr, hour);

    if (state !== 'free') return;
    if (myFutureCount >= 2) {
      setError('Du kan inte ha fler än 2 kommande bokningar.');
      return;
    }
    if (!courtId) return;

    setLoadingSlot(slotKey);
    setError(null);
    try {
      await bookingsApi.create({ courtId: Number(courtId), date: dateStr, startHour: hour });
      setConfirmedSlot(slotKey);
      await loadBookings();
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: string | { title?: string } } };
      const data = axiosErr?.response?.data;
      const msg = typeof data === 'string' ? data : data?.title;
      setError(msg ?? 'Bokning misslyckades. Försök igen.');
    } finally {
      setLoadingSlot(null);
    }
  }

  const atBookingLimit = myFutureCount >= 2;

  return (
    <div className="weekly-page">
      <div className="weekly-header">
        <div className="weekly-title">
          <h1>Boka {court?.name ?? '...'}</h1>
          {court && (
            <span className={`surface-badge ${court.surface.toLowerCase()}`}>{court.surface}</span>
          )}
        </div>
        <div className="week-nav">
          <button
            className="btn-secondary"
            onClick={() => { setWeekStart(w => addDays(w, -7)); setConfirmedSlot(null); }}
          >
            ← Förra veckan
          </button>
          <span className="week-label">
            {formatDate(weekDays[0])} – {formatDate(weekDays[6])}
          </span>
          <button
            className="btn-secondary"
            onClick={() => { setWeekStart(w => addDays(w, 7)); setConfirmedSlot(null); }}
          >
            Nästa vecka →
          </button>
        </div>
      </div>

      {atBookingLimit && (
        <div className="booking-limit-notice">
          Du har redan 2 kommande bokningar. Avboka en för att kunna boka igen.{' '}
          <button className="link-btn" onClick={() => navigate('/my-bookings')}>Mina bokningar</button>
        </div>
      )}

      {error && <div className="error-msg">{error}</div>}

      {confirmedSlot && (
        <div className="success-notice">
          ✅ Bokning bekräftad!{' '}
          <button className="link-btn" onClick={() => navigate('/my-bookings')}>Visa mina bokningar</button>
        </div>
      )}

      <div className="calendar-scroll">
        <div className="calendar-grid">
          {/* Header row */}
          <div className="time-header" />
          {weekDays.map((day, i) => {
            const ds = toDateStr(day);
            const todayStr = toDateStr(new Date());
            return (
              <div key={i} className={`day-header${ds === todayStr ? ' today' : ''}`}>
                <span className="day-name">{DAY_NAMES[i]}</span>
                <span className="day-date">{formatDate(day)}</span>
              </div>
            );
          })}

          {/* Time rows */}
          {HOURS.map(hour => (
            <>
              <div key={`label-${hour}`} className="time-label">{hour}:00</div>
              {weekDays.map((day, di) => {
                const dateStr = toDateStr(day);
                const slotKey = `${dateStr}-${hour}`;
                const { state, booking } = getSlotInfo(dateStr, hour);
                const isLoading = loadingSlot === slotKey;
                const isConfirmed = confirmedSlot === slotKey;

                return (
                  <div
                    key={`${di}-${hour}`}
                    className={`slot slot-${state}${isLoading ? ' slot-loading' : ''}${isConfirmed ? ' slot-confirmed' : ''}${state === 'free' && !atBookingLimit ? ' slot-clickable' : ''}`}
                    onClick={() => handleSlotClick(dateStr, hour)}
                    role={state === 'free' && !atBookingLimit ? 'button' : undefined}
                    title={
                      state === 'taken' && booking
                        ? `${booking.userName}${booking.userPhone ? ` · ${booking.userPhone}` : ''}`
                        : undefined
                    }
                  >
                    {isLoading && <span className="slot-spinner">⏳</span>}
                    {state === 'mine' && !isLoading && (
                      <span className="slot-label">Du</span>
                    )}
                    {state === 'taken' && !isLoading && booking && (
                      <span className="slot-label">
                        <span className="slot-name">{booking.userName}</span>
                        {booking.userPhone && (
                          <span className="slot-phone">{booking.userPhone}</span>
                        )}
                      </span>
                    )}
                    {state === 'free' && !isLoading && !atBookingLimit && (
                      <span className="slot-free-icon">+</span>
                    )}
                  </div>
                );
              })}
            </>
          ))}
        </div>
      </div>

      <div className="calendar-legend">
        <span className="legend-item"><span className="legend-swatch swatch-free" />Ledig</span>
        <span className="legend-item"><span className="legend-swatch swatch-mine" />Din bokning</span>
        <span className="legend-item"><span className="legend-swatch swatch-taken" />Bokad</span>
        <span className="legend-item"><span className="legend-swatch swatch-past" />Passerad</span>
      </div>
    </div>
  );
}
