import { useAuth } from 'react-oidc-context';
import { Link } from 'react-router-dom';
import './LandingPage.css';

export function LandingPage() {
  const auth = useAuth();

  return (
    <div className="landing">
      {/* Hero */}
      <section className="hero">
        <div className="hero-content">
          <h1>Välkommen till<br /><span>Jonsereds TK</span></h1>
          <p className="hero-tagline">
            Boka en bana, träffa vänner och njut av tennis i hjärtat av Jonsered.
          </p>
          <div className="hero-cta">
            {auth.isAuthenticated ? (
              <Link to="/book/1" className="cta-btn primary">Boka Banan →</Link>
            ) : (
              <>
                <Link to="/book/1" className="cta-btn primary">Se lediga tider →</Link>
                <button className="cta-btn secondary" onClick={() => auth.signinRedirect()}>
                  Logga in & boka
                </button>
              </>
            )}
          </div>
        </div>
        <div className="hero-image">🎾</div>
      </section>

      {/* About */}
      <section id="om-oss" className="about">
        <div className="section-content">
          <h2>Om Jonsereds TK</h2>
          <p>
            Jonsereds Tennisklubb är en familjevänlig klubb som har erbjudit
            tennisglädje sedan 1970. Vi har en välskött asfaltbana och välkomnar
            spelare på alla nivåer — från nybörjare till erfarna tävlingsspelare.
          </p>
          <div className="about-highlights">
            <div className="highlight">
              <span className="highlight-icon">🏆</span>
              <strong>Tävlingsaktiv</strong>
              <p>Vi deltar i SDS-serien och arrangerar lokala cuper.</p>
            </div>
            <div className="highlight">
              <span className="highlight-icon">👶</span>
              <strong>Träning för alla</strong>
              <p>Juniorträning, vuxenkurser och individuella lektioner.</p>
            </div>
            <div className="highlight">
              <span className="highlight-icon">🌿</span>
              <strong>Vacker miljö</strong>
              <p>Belägen i naturskön omgivning vid Säveån.</p>
            </div>
          </div>
        </div>
      </section>

      {/* Court */}
      <section id="banan" className="courts-section">
        <div className="section-content">
          <h2>Banan</h2>
          <div className="court-card-single">
            <div className="court-icon">⬛</div>
            <h3>Asfaltbana</h3>
            <span className="surface-badge asphalt">Asphalt</span>
            <p>Utomhusbana med asfaltunderlag.</p>
            <Link to="/book/1" className="cta-btn primary">Se lediga tider →</Link>
          </div>
          {!auth.isAuthenticated && (
            <div className="courts-cta">
              <p>Logga in för att boka banan</p>
              <button className="cta-btn primary" onClick={() => auth.signinRedirect()}>
                Logga in
              </button>
            </div>
          )}
        </div>
      </section>

      {/* Contact */}
      <section id="kontakt" className="contact">
        <div className="section-content">
          <h2>Kontakt & Hitta hit</h2>
          <div className="contact-grid">
            <div>
              <p>📍 Tennisvägen 1, 443 30 Lerum</p>
              <p>📧 <a href="mailto:info@jonsereds-tk.se">info@jonsereds-tk.se</a></p>
              <p>📞 0302-123 45</p>
            </div>
            <div>
              <p><strong>Öppettider</strong></p>
              <p>Mån–fre: 07:00–21:00</p>
              <p>Lör–sön: 08:00–20:00</p>
            </div>
          </div>
        </div>
      </section>
    </div>
  );
}
