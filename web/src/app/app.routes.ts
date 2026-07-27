import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/auth/login.component').then((m) => m.LoginComponent),
  },
  {
    path: '',
    canActivate: [authGuard],
    children: [
      {
        path: '',
        loadComponent: () =>
          import('./features/showcase/showcase.component').then((m) => m.ShowcaseComponent),
      },
      {
        path: 'catalog',
        loadComponent: () =>
          import('./features/catalog/catalog.component').then((m) => m.CatalogComponent),
      },
      {
        path: 'items/new',
        loadComponent: () =>
          import('./features/item-detail/item-detail.component').then((m) => m.ItemDetailComponent),
      },
      {
        path: 'items/:id',
        loadComponent: () =>
          import('./features/item-detail/item-detail.component').then((m) => m.ItemDetailComponent),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
