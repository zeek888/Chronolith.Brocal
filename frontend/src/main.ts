import { initFederation } from '@angular-architects/native-federation';

// Standalone application. Inside ChronoLith this entry point is not used: the Shell loads `./routes`
// and the widgets from remoteEntry.json.
initFederation(
  {},
  {
    hostRemoteEntry: { url: './remoteEntry.json' },
  },
)
  .catch((err) => console.error(err))
  .then(() => import('./bootstrap'))
  .catch((err) => console.error(err));
