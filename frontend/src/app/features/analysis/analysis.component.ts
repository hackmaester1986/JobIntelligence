import { Component, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgFor, NgIf } from '@angular/common';
import { toObservable } from '@angular/core/rxjs-interop';
import { Subscription } from 'rxjs';
import { skip } from 'rxjs/operators';
import { BaseChartDirective } from 'ng2-charts';
import { ChartData, ChartOptions } from 'chart.js';
import { MatCardModule } from '@angular/material/card';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AnalysisService, DistributionBin } from '../../core/services/analysis.service';
import { LocationFilterService } from '../../core/services/location-filter.service';

interface MetricDef {
  value: string;
  label: string;
}

@Component({
  selector: 'app-analysis',
  standalone: true,
  imports: [
    FormsModule, NgFor, NgIf,
    BaseChartDirective,
    MatCardModule, MatSelectModule, MatFormFieldModule, MatCheckboxModule, MatProgressSpinnerModule
  ],
  templateUrl: './analysis.component.html',
  styleUrl: './analysis.component.scss'
})
export class AnalysisComponent implements OnInit, OnDestroy {
  private analysisService = inject(AnalysisService);
  readonly locationFilter = inject(LocationFilterService);

  readonly metrics: MetricDef[] = [
    { value: 'activeJobs',           label: 'Active Job Postings' },
    { value: 'remoteJobs',           label: 'Remote Positions' },
    { value: 'salaryDisclosureRate', label: 'Salary Disclosure Rate' },
    { value: 'avgJobLifetimeDays',   label: 'Avg Job Lifetime (Days)' },
    { value: 'duplicateJobs',        label: 'Duplicate Postings' },
  ];

  selectedMetric = 'activeJobs';
  availableSizes = signal<string[]>([]);
  selectedSizes = new Set<string>();
  loading = signal(true);

  chartData = signal<ChartData<'bar'>>({ labels: [], datasets: [] });

  chartOptions: ChartOptions<'bar'> = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: { display: false },
      tooltip: {
        callbacks: {
          label: ctx => ` ${(ctx.parsed.y ?? 0).toLocaleString()} companies`
        }
      }
    },
    scales: {
      x: { ticks: { font: { size: 12 }, maxRotation: 45, minRotation: 45 } },
      y: { beginAtZero: true, ticks: { precision: 0 }, title: { display: true, text: 'Companies' } }
    }
  };

  private locationSub!: Subscription;

  constructor() {
    this.locationSub = toObservable(this.locationFilter.usOnly).pipe(skip(1)).subscribe(() => this.load());
  }

  ngOnInit(): void {
    this.analysisService.getSizes().subscribe(sizes => {
      this.availableSizes.set(sizes);
      this.load();
    });
  }

  ngOnDestroy(): void {
    this.locationSub.unsubscribe();
  }

  toggleSize(size: string): void {
    if (this.selectedSizes.has(size)) this.selectedSizes.delete(size);
    else this.selectedSizes.add(size);
    this.load();
  }

  isSizeSelected(size: string): boolean {
    return this.selectedSizes.has(size);
  }

  get currentMetricLabel(): string {
    return this.metrics.find(m => m.value === this.selectedMetric)?.label ?? '';
  }

  onMetricChange(): void {
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    const isUs = this.locationFilter.usOnly() ? true : undefined;
    this.analysisService.getDistribution(this.selectedMetric, [...this.selectedSizes], isUs).subscribe({
      next: bins => {
        this.chartData.set({
          labels: bins.map(b => b.bucket),
          datasets: [{
            data: bins.map(b => b.count),
            backgroundColor: 'rgba(63, 81, 181, 0.75)',
            borderColor: 'rgba(63, 81, 181, 1)',
            borderWidth: 1,
            borderRadius: 4,
          }]
        });
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }
}
