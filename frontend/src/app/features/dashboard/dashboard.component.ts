import { Component, effect, inject, OnDestroy, signal } from '@angular/core';
import { AsyncPipe, DecimalPipe, NgIf } from '@angular/common';
import { Subscription } from 'rxjs';
import { toObservable } from '@angular/core/rxjs-interop';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { DashboardStats } from '../../core/models/stats.model';
import { StatsService } from '../../core/services/stats.service';
import { LocationFilterService } from '../../core/services/location-filter.service';
import { TopCompaniesChartComponent } from './components/top-companies-chart/top-companies-chart.component';
import { SeniorityChartComponent } from './components/seniority-chart/seniority-chart.component';
import { DepartmentsChartComponent } from './components/departments-chart/departments-chart.component';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [
    NgIf, AsyncPipe, DecimalPipe,
    MatCardModule, MatProgressSpinnerModule,
    TopCompaniesChartComponent, SeniorityChartComponent,
    DepartmentsChartComponent
  ],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss'
})
export class DashboardComponent implements OnDestroy {
  private statsService = inject(StatsService);
  private locationFilter = inject(LocationFilterService);

  stats = signal<DashboardStats | null>(null);
  loading = signal(true);
  error = signal<string | null>(null);

  private sub: Subscription;

  constructor() {
    this.sub = toObservable(this.locationFilter.usOnly).subscribe(() => this.load());
  }

  ngOnDestroy(): void {
    this.sub.unsubscribe();
  }

  private load(): void {
    this.loading.set(true);
    this.statsService.getStats(this.locationFilter.usOnly() ? true : undefined).subscribe({
      next: s => { this.stats.set(s); this.loading.set(false); },
      error: () => { this.error.set('Failed to load stats'); this.loading.set(false); }
    });
  }

  get remotePercent(): number {
    const s = this.stats();
    if (!s || s.totalActiveJobs === 0) return 0;
    return Math.round((s.remoteJobs / s.totalActiveJobs) * 100);
  }
}
