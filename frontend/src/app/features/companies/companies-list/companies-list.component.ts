import { Component, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { DecimalPipe, NgFor, NgIf } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { Subject, Subscription } from 'rxjs';
import { debounceTime, distinctUntilChanged } from 'rxjs/operators';
import { Company } from '../../../core/models/company.model';
import { CompaniesService } from '../../../core/services/companies.service';
import { PagedResult } from '../../../core/services/jobs.service';

@Component({
  selector: 'app-companies-list',
  standalone: true,
  imports: [
    RouterLink, FormsModule, NgFor, NgIf, DecimalPipe,
    MatCardModule, MatInputModule,
    MatFormFieldModule, MatProgressSpinnerModule, MatPaginatorModule,
    MatCheckboxModule
  ],
  templateUrl: './companies-list.component.html',
  styleUrl: './companies-list.component.scss'
})
export class CompaniesListComponent implements OnInit, OnDestroy {
  private companiesService = inject(CompaniesService);
  private searchSubject = new Subject<string>();
  private searchSub!: Subscription;

  result        = signal<PagedResult<Company> | null>(null);
  loading       = signal(true);
  industries    = signal<string[]>([]);
  selectedIndustries = new Set<string>();
  searchQ = '';
  page    = 1;
  pageSize = 50;

  ngOnInit(): void {
    this.searchSub = this.searchSubject.pipe(
      debounceTime(300),
      distinctUntilChanged()
    ).subscribe(() => {
      this.page = 1;
      this.load();
    });

    this.companiesService.getIndustries().subscribe(list => this.industries.set(list));
    this.load();
  }

  ngOnDestroy(): void { this.searchSub.unsubscribe(); }

  onSearchInput(value: string): void {
    this.searchQ = value;
    this.searchSubject.next(value);
  }

  toggleIndustry(industry: string): void {
    if (this.selectedIndustries.has(industry)) {
      this.selectedIndustries.delete(industry);
    } else {
      this.selectedIndustries.add(industry);
    }
    this.page = 1;
    this.load();
  }

  isSelected(industry: string): boolean {
    return this.selectedIndustries.has(industry);
  }

  load(): void {
    this.loading.set(true);
    const industries = this.selectedIndustries.size > 0 ? [...this.selectedIndustries] : undefined;
    this.companiesService.getCompanies(this.searchQ || undefined, this.page, this.pageSize, industries).subscribe({
      next: r => { this.result.set(r); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  onPage(e: PageEvent): void {
    this.page = e.pageIndex + 1;
    this.pageSize = e.pageSize;
    this.load();
  }
}
