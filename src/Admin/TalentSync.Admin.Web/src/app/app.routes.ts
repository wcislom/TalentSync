import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'scenarios' },
  { path: 'scenarios', loadComponent: () => import('./scenarios/scenarios-page').then((m) => m.ScenariosPage) },
];
