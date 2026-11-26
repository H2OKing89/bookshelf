import { Component } from 'react';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import translate from 'Utilities/String/translate';
import AboutConnector from './About/AboutConnector';
import DiskSpaceConnector from './DiskSpace/DiskSpaceConnector';
import HealthConnector from './Health/HealthConnector';
import MetadataProviders from './MetadataProviders/MetadataProviders';
import MoreInfo from './MoreInfo/MoreInfo';
import PerformanceProfiling from './Performance/PerformanceProfiling';

class Status extends Component {

  //
  // Render

  render() {
    return (
      <PageContent title={translate('Status')}>
        <PageContentBody>
          <HealthConnector />
          <MetadataProviders />
          <PerformanceProfiling />
          <DiskSpaceConnector />
          <AboutConnector />
          <MoreInfo />
        </PageContentBody>
      </PageContent>
    );
  }

}

export default Status;
