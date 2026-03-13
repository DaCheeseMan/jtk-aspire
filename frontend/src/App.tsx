import { useEffect } from 'react'
import { Route, Routes } from 'react-router-dom'
import { useAuth } from 'react-oidc-context'
import { Navbar } from './components/Navbar'
import { ProtectedRoute } from './components/ProtectedRoute'
import { LandingPage } from './pages/LandingPage'
import { CourtsPage } from './pages/CourtsPage'
import { WeeklyCalendarPage } from './pages/WeeklyCalendarPage'
import { MyBookingsPage } from './pages/MyBookingsPage'
import { setAuthToken, setupAuthHandlers } from './api/client'
import './App.css'

function App() {
  const auth = useAuth()

  // Keep the Axios token in sync and register auth handlers for the 401 interceptor
  useEffect(() => {
    setAuthToken(auth.user?.access_token ?? null)
    setupAuthHandlers(
      () => auth.signinSilent(),
      () => auth.signoutRedirect(),
    )
  }, [auth, auth.user?.access_token])

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
            <ProtectedRoute><WeeklyCalendarPage /></ProtectedRoute>
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

