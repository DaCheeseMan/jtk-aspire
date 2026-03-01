import axios from 'axios';

const apiClient = axios.create({ baseURL: '/api' });

export function setAuthToken(token: string | null) {
  if (token) {
    apiClient.defaults.headers.common['Authorization'] = `Bearer ${token}`;
  } else {
    delete apiClient.defaults.headers.common['Authorization'];
  }
}

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
  date: string;
  startTime: string;
  endTime: string;
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
};
