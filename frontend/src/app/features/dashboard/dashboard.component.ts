import { Component, inject, OnInit, signal } from '@angular/core';
import { AsyncPipe, DecimalPipe, NgIf } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { DashboardStats } from '../../core/models/stats.model';
import { StatsService } from '../../core/services/stats.service';
import { TopCompaniesChartComponent } from './components/top-companies-chart/top-companies-chart.component';
import { SeniorityChartComponent } from './components/seniority-chart/seniority-chart.component';
import { DepartmentsChartComponent } from './components/departments-chart/departments-chart.component';
import { AiChatComponent } from './components/ai-chat/ai-chat.component';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [
    NgIf, AsyncPipe, DecimalPipe,
    MatCardModule, MatProgressSpinnerModule,
    TopCompaniesChartComponent, SeniorityChartComponent,
    DepartmentsChartComponent, AiChatComponent
  ],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss'
})
export class DashboardComponent implements OnInit {
  private statsService = inject(StatsService);

  stats = signal<DashboardStats | null>(null);
  loading = signal(true);
  error = signal<string | null>(null);

  ngOnInit(): void {
    this.statsService.getStats().subscribe({
      next: s => { this.stats.set(s); this.loading.set(false); },
      error: e => { this.error.set('Failed to load stats'); this.loading.set(false); }
    });
  }

  get remotePercent(): number {
    const s = this.stats();
    if (!s || s.totalActiveJobs === 0) return 0;
    return Math.round((s.remoteJobs / s.totalActiveJobs) * 100);
  }
}
