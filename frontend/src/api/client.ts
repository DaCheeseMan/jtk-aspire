import axios from 'axios';

const apiClient = axios.create({ baseURL: '/api' });

export function setAuthToken(token: string | null) {
  if (token) {
    apiClient.defaults.headers.common['Authorization'] = `Bearer ${token}`;
  } else {
    delete apiClient.defaults.headers.common['Authorization'];
  }
}

type SigninSilentFn = () => Promise<{ access_token: string } | null | undefined>;
type SignoutFn = () => Promise<void>;

let _signinSilent: SigninSilentFn | null = null;
let _signout: SignoutFn | null = null;

export function setupAuthHandlers(signinSilent: SigninSilentFn, signout: SignoutFn) {
  _signinSilent = signinSilent;
  _signout = signout;
}

// On 401, try a silent token renewal and retry the request once.
// If renewal fails, sign the user out.
apiClient.interceptors.response.use(
  res => res,
  async (error) => {
    const original = error.config;
    if (error.response?.status === 401 && !original._retry && _signinSilent) {
      original._retry = true;
      try {
        const user = await _signinSilent();
        if (user?.access_token) {
          setAuthToken(user.access_token);
          original.headers['Authorization'] = `Bearer ${user.access_token}`;
          return apiClient(original);
        }
      } catch {
        // Silent renewal failed — sign out so the user can log in again
        await _signout?.();
        return Promise.reject(error);
      }
    }
    return Promise.reject(error);
  }
);

export interface Court {
  id: number;
  name: string;
  surface: string;
  description: string;
}

export interface Booking {
  id: number;
  courtId: number;
  court: Court;
  userId: string;
  userName: string;
  userPhone: string;
  date: string;
  startTime: string;
  endTime: string;
}

export interface CourtBooking {
  id: number;
  date: string;
  startTime: string;
  endTime: string;
  userId: string;
  userName: string;
  userPhone: string;
}

export interface CreateBookingRequest {
  courtId: number;
  date: string;
  startHour: number;
}

export const courtsApi = {
  getAll: () => apiClient.get<Court[]>('/courts').then(r => r.data),
  getById: (id: number) => apiClient.get<Court>(`/courts/${id}`).then(r => r.data),
};

export const bookingsApi = {
  getMine: () => apiClient.get<Booking[]>('/bookings').then(r => r.data),
  create: (req: CreateBookingRequest) => apiClient.post<Booking>('/bookings', req).then(r => r.data),
  cancel: (id: number) => apiClient.delete(`/bookings/${id}`),
  getForCourt: (courtId: number, from: string, to: string) =>
    apiClient.get<CourtBooking[]>(`/courts/${courtId}/bookings`, { params: { from, to } }).then(r => r.data),
};
