import classNames from 'classnames';
import { Component } from 'react';
import SelectInput from 'Components/Form/SelectInput';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import { icons } from 'Helpers/Props';
import styles from './TablePager.css';

export interface TablePagerProps {
  page?: number;
  totalPages?: number;
  totalRecords?: number;
  isFetching?: boolean;
  onFirstPagePress: () => void;
  onPreviousPagePress: () => void;
  onNextPagePress: () => void;
  onLastPagePress: () => void;
  onPageSelect: (page: number) => void;
}

class TablePager extends Component<TablePagerProps, { isShowingPageSelect: boolean }> {

  //
  // Lifecycle

  constructor(props: TablePagerProps) {
    super(props);

    this.state = {
      isShowingPageSelect: false
    };
  }

  //
  // Listeners

  onOpenPageSelectClick = () => {
    this.setState({ isShowingPageSelect: true });
  };

  onPageSelect = ({ value }: { value: string | number }) => {
    this.setState({ isShowingPageSelect: false });
    const page = Number(value);
    if (Number.isSafeInteger(page) && page > 0 && page <= (this.props.totalPages ?? 0)) {
      this.props.onPageSelect(page);
    }
  };

  onPageSelectBlur = () => {
    this.setState({ isShowingPageSelect: false });
  };

  //
  // Render

  render() {
    const {
      page,
      totalPages = 0,
      totalRecords,
      isFetching,
      onFirstPagePress,
      onPreviousPagePress,
      onNextPagePress,
      onLastPagePress
    } = this.props;

    const isShowingPageSelect = this.state.isShowingPageSelect;
    if (!page || !Number.isSafeInteger(totalPages) || totalPages < 1) {
      return null;
    }
    const pages = Array.from({ length: totalPages }, (_, i) => {
      const pageNumber = i + 1;

      return {
        key: pageNumber,
        value: pageNumber
      };
    });

    const isFirstPage = page === 1;
    const isLastPage = page === totalPages;

    return (
      <div className={styles.pager}>
        <div className={styles.loadingContainer}>
          {
            isFetching &&
              <LoadingIndicator
                className={styles.loading}
                size={20}
              />
          }
        </div>

        <div className={styles.controlsContainer}>
          <div className={styles.controls}>
            <Link
              className={classNames(
                styles.pageLink,
                isFirstPage && styles.disabledPageButton
              )}
              isDisabled={isFirstPage}
              onPress={onFirstPagePress}
            >
              <Icon name={icons.PAGE_FIRST} />
            </Link>

            <Link
              className={classNames(
                styles.pageLink,
                isFirstPage && styles.disabledPageButton
              )}
              isDisabled={isFirstPage}
              onPress={onPreviousPagePress}
            >
              <Icon name={icons.PAGE_PREVIOUS} />
            </Link>

            <div className={styles.pageNumber}>
              {
                !isShowingPageSelect &&
                  <Link
                    isDisabled={totalPages === 1}
                    onPress={this.onOpenPageSelectClick}
                  >
                    {page} / {totalPages}
                  </Link>
              }

              {
                isShowingPageSelect &&
                  <SelectInput
                    className={styles.pageSelect}
                    name="pageSelect"
                    value={page}
                    values={pages}
                    autoFocus={true}
                    onChange={this.onPageSelect}
                    onBlur={this.onPageSelectBlur}
                  />
              }
            </div>

            <Link
              className={classNames(
                styles.pageLink,
                isLastPage && styles.disabledPageButton
              )}
              isDisabled={isLastPage}
              onPress={onNextPagePress}
            >
              <Icon name={icons.PAGE_NEXT} />
            </Link>

            <Link
              className={classNames(
                styles.pageLink,
                isLastPage && styles.disabledPageButton
              )}
              isDisabled={isLastPage}
              onPress={onLastPagePress}
            >
              <Icon name={icons.PAGE_LAST} />
            </Link>
          </div>
        </div>

        <div className={styles.recordsContainer}>
          <div className={styles.records}>
            Total records: {totalRecords}
          </div>
        </div>
      </div>
    );
  }

}

export default TablePager;
