import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
  {
    path: 'dashboard',
    loadComponent: () =>
      import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent)
  },
  {
    path: 'jobs',
    loadComponent: () =>
      import('./features/jobs/jobs-list/jobs-list.component').then(m => m.JobsListComponent)
  },
  {
    path: 'jobs/:id',
    loadComponent: () =>
      import('./features/jobs/job-detail/job-detail.component').then(m => m.JobDetailComponent)
  },
  {
    path: 'companies',
    loadComponent: () =>
      import('./features/companies/companies-list/companies-list.component').then(m => m.CompaniesListComponent)
  },
  {
    path: 'companies/:id',
    loadComponent: () =>
      import('./features/companies/company-detail/company-detail.component').then(m => m.CompanyDetailComponent)
  },
  {
    path: 'companies/:id/jobs',
    loadComponent: () =>
      import('./features/companies/company-jobs/company-jobs.component').then(m => m.CompanyJobsComponent)
  },
  {
    path: 'analysis',
    loadComponent: () =>
      import('./features/analysis/analysis.component').then(m => m.AnalysisComponent)
  }
];
