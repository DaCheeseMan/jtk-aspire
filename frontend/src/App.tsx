import { Route, Routes } from 'react-router-dom'
import { useAuth } from 'react-oidc-context'
import { Navbar } from './components/Navbar'
import { ProtectedRoute } from './components/ProtectedRoute'
import { LandingPage } from './pages/LandingPage'
import { CourtsPage } from './pages/CourtsPage'
import { BookingPage } from './pages/BookingPage'
import { MyBookingsPage } from './pages/MyBookingsPage'
import './App.css'

function App() {
  const auth = useAuth()

  // Handle OIDC callback (return from Keycloak)
  if (auth.isLoading) {
    return <div className="app-loading">Laddar...</div>
  }

  if (auth.error) {
    return <div className="app-error">Autentiseringsfel: {auth.error.message}</div>
  }

  return (
    <div className="app">
      <Navbar />
      <main>
        <Routes>
          <Route path="/" element={<LandingPage />} />
          <Route path="/courts" element={
            <ProtectedRoute><CourtsPage /></ProtectedRoute>
          } />
          <Route path="/book/:courtId" element={
            <ProtectedRoute><BookingPage /></ProtectedRoute>
          } />
          <Route path="/my-bookings" element={
            <ProtectedRoute><MyBookingsPage /></ProtectedRoute>
          } />
        </Routes>
      </main>
    </div>
  )
}

export default App

