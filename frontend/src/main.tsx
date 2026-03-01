import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { AuthProvider } from 'react-oidc-context'
import { BrowserRouter } from 'react-router-dom'
import './index.css'
import App from './App.tsx'

// Keycloak OIDC config — authority resolves via Aspire service discovery in dev
const keycloakBase = import.meta.env.VITE_KEYCLOAK_URL ?? 'http://localhost:8080'

const oidcConfig = {
  authority: `${keycloakBase}/realms/jtk`,
  client_id: 'jtk-web',
  redirect_uri: window.location.origin,
  post_logout_redirect_uri: window.location.origin,
  response_type: 'code',
  scope: 'openid profile email',
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AuthProvider {...oidcConfig}>
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </AuthProvider>
  </StrictMode>,
)

