import { bootstrapApplication } from '@angular/platform-browser';
import { loadRuntimeConfig } from './app/core/config';
import { StandaloneApp, createStandaloneConfig } from './app/standalone/standalone.config';

loadRuntimeConfig()
  .then((config) => bootstrapApplication(StandaloneApp, createStandaloneConfig(config)))
  .catch((error: unknown) => {
    console.error(error);
    const status = document.querySelector('.br-splash__status');
    if (status) {
      status.textContent = 'No fue posible iniciar Brocal. Verifica la configuración e intenta de nuevo.';
    }
  });
