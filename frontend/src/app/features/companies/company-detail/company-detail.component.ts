import { Component, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { DatePipe, DecimalPipe, NgIf, PercentPipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { toObservable } from '@angular/core/rxjs-interop';
import { Subscription } from 'rxjs';
import { skip } from 'rxjs/operators';
import { Company, SnapshotPoint } from '../../../core/models/company.model';
import { CompaniesService } from '../../../core/services/companies.service';
import { LocationFilterService } from '../../../core/services/location-filter.service';
import { CompanyHiringChartComponent } from './company-hiring-chart/company-hiring-chart.component';

@Component({
  selector: 'app-company-detail',
  standalone: true,
  imports: [
    NgIf, RouterLink, DecimalPipe, PercentPipe, DatePipe,
    MatCardModule, MatButtonModule, MatButtonToggleModule, MatProgressSpinnerModule,
    CompanyHiringChartComponent
  ],
  templateUrl: './company-detail.component.html',
  styleUrl: './company-detail.component.scss'
})
export class CompanyDetailComponent implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private companiesService = inject(CompaniesService);
  locationFilter = inject(LocationFilterService);

  private locationSub!: Subscription;

  company       = signal<Company | null>(null);
  loading       = signal(true);
  snapshots     = signal<SnapshotPoint[]>([]);
  chartsLoading = signal(true);
  range         = signal('1m');
  companyId     = 0;
  hasAnalysisState = false;

  constructor() {
    this.locationSub = toObservable(this.locationFilter.usOnly).pipe(skip(1)).subscribe(() => this.loadCompany());
  }

  ngOnInit(): void {
    this.hasAnalysisState = !!history.state?.fromAnalysis;
    this.companyId = Number(this.route.snapshot.paramMap.get('id'));
    this.loadCompany();
    this.loadSnapshots('1m');
  }

  ngOnDestroy(): void {
    this.locationSub?.unsubscribe();
  }

  private loadCompany(): void {
    this.loading.set(true);
    const isUs = this.locationFilter.usOnly() ? true : undefined;
    this.companiesService.getCompany(this.companyId, isUs).subscribe({
      next: c => { this.company.set(c); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  loadSnapshots(range: string): void {
    this.chartsLoading.set(true);
    this.companiesService.getSnapshots(this.companyId, range).subscribe({
      next: s => { this.snapshots.set(s); this.chartsLoading.set(false); },
      error: () => this.chartsLoading.set(false)
    });
  }

  onRangeChange(r: string): void {
    this.range.set(r);
    this.loadSnapshots(r);
  }

  goBack(): void {
    this.router.navigate(['/analysis']);
  }
}
