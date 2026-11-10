// main.js - NtfsAudit Landing Page Client Script

document.addEventListener('DOMContentLoaded', () => {
  // --- Language Switcher ---
  const langButtons = document.querySelectorAll('.btn-lang-toggle');
  
  // Get preferred language from localStorage or default to English ('en')
  let currentLang = localStorage.getItem('onlyrights-landing-lang') || 'en';
  setLanguage(currentLang);

  langButtons.forEach(btn => {
    btn.addEventListener('click', () => {
      const selectedLang = btn.getAttribute('data-lang');
      setLanguage(selectedLang);
    });
  });

  function setLanguage(lang) {
    currentLang = lang;
    localStorage.setItem('onlyrights-landing-lang', lang);
    
    // Set class on body
    document.body.className = lang;

    // Update active button indicators if any
    langButtons.forEach(btn => {
      if (btn.getAttribute('data-lang') === lang) {
        btn.classList.add('active-lang');
        btn.style.borderColor = 'var(--accent-primary)';
        btn.style.color = 'var(--accent-primary)';
      } else {
        btn.classList.remove('active-lang');
        btn.style.borderColor = 'var(--border-glass)';
        btn.style.color = 'var(--text-main)';
      }
    });
  }

  // --- Mobile Menu Toggle ---
  const menuToggle = document.getElementById('menu-toggle');
  const navMenu = document.getElementById('nav-menu');

  if (menuToggle && navMenu) {
    menuToggle.addEventListener('click', () => {
      navMenu.classList.toggle('mobile-active');
      // Simple toggle animation for menu burger lines
      const spans = menuToggle.querySelectorAll('span');
      if (navMenu.classList.contains('mobile-active')) {
        spans[0].style.transform = 'rotate(45deg) translate(6px, 6px)';
        spans[1].style.opacity = '0';
        spans[2].style.transform = 'rotate(-45deg) translate(5px, -5px)';
      } else {
        spans[0].style.transform = 'none';
        spans[1].style.opacity = '1';
        spans[2].style.transform = 'none';
      }
    });

    // Close menu when clicking nav link
    const navLinks = navMenu.querySelectorAll('.nav-link');
    navLinks.forEach(link => {
      link.addEventListener('click', () => {
        navMenu.classList.remove('mobile-active');
        const spans = menuToggle.querySelectorAll('span');
        spans[0].style.transform = 'none';
        spans[1].style.opacity = '1';
        spans[2].style.transform = 'none';
      });
    });
  }

  // --- Interactive Workflows Gallery ---
  const galleryTabs = document.querySelectorAll('.gallery-tab');
  const galleryPanes = document.querySelectorAll('.gallery-content-pane');

  galleryTabs.forEach(tab => {
    tab.addEventListener('click', () => {
      const targetPaneId = tab.getAttribute('data-target');
      
      // Update active tab
      galleryTabs.forEach(t => t.classList.remove('active'));
      tab.classList.add('active');

      // Update active pane
      galleryPanes.forEach(pane => {
        pane.classList.remove('active');
        if (pane.id === targetPaneId) {
          pane.classList.add('active');
        }
      });
    });
  });

  // --- Copy to Clipboard Tool ---
  const btnCopy = document.getElementById('btn-copy');
  const copyCode = document.getElementById('copy-code');

  if (btnCopy && copyCode) {
    btnCopy.addEventListener('click', () => {
      const codeText = copyCode.textContent.trim();
      
      navigator.clipboard.writeText(codeText).then(() => {
        // Temp success feedback
        const origTextEn = "Copy";
        const origTextIt = "Copia";
        const isIt = document.body.classList.contains('it');
        
        btnCopy.textContent = isIt ? 'Copiato!' : 'Copied!';
        btnCopy.style.borderColor = 'var(--accent-primary)';
        btnCopy.style.color = 'var(--accent-primary)';
        
        setTimeout(() => {
          btnCopy.textContent = isIt ? origTextIt : origTextEn;
          btnCopy.style.borderColor = 'var(--border-glass)';
          btnCopy.style.color = 'var(--text-muted)';
        }, 2000);
      }).catch(err => {
        console.error('Failed to copy text: ', err);
      });
    });
  }
});
